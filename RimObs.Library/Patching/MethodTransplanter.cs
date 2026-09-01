using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using RimWorks.RimObs.Profile;
using HarmonyLib;

namespace RimWorks.RimObs.Patching;

internal static class MethodTransplanter {
    private static readonly MethodInfo s_StartByIdMethod = typeof(Profiler).GetMethod(
        nameof(Profiler.StartById),
        BindingFlags.Public | BindingFlags.Static
    ) ?? throw new InvalidOperationException("Profiler.StartById not found.");

    private static readonly MethodInfo s_StopByIdMethod = typeof(Profiler).GetMethod(
        nameof(Profiler.StopById),
        BindingFlags.Public | BindingFlags.Static
    ) ?? throw new InvalidOperationException("Profiler.StopById not found.");

    public static MethodInfo TranspilerMethod { get; } = typeof(MethodTransplanter).GetMethod(
        nameof(Transpile),
        BindingFlags.Public | BindingFlags.Static
    ) ?? throw new InvalidOperationException("MethodTransplanter.Transpile not found.");

    public static IEnumerable<CodeInstruction> Transpile(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator,
        MethodBase __originalMethod
    ) {
        if (!SectionCatalog.TryGetSectionId(__originalMethod, out int sectionId))
            return instructions;

        return Instrument(instructions, generator, __originalMethod, sectionId);
    }

    private static IEnumerable<CodeInstruction> Instrument(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator,
        MethodBase originalMethod,
        int sectionId
    ) {
        Type returnType = originalMethod is MethodInfo mi ? mi.ReturnType : typeof(void);
        bool hasReturn = returnType != typeof(void);

        LocalBuilder tokenLocal = generator.DeclareLocal(typeof(long));
        LocalBuilder? returnLocal = hasReturn ? generator.DeclareLocal(returnType) : null;
        Label endLabel = generator.DefineLabel();

        yield return new CodeInstruction(OpCodes.Ldc_I4, sectionId);
        yield return new CodeInstruction(OpCodes.Call, s_StartByIdMethod);
        yield return new CodeInstruction(OpCodes.Stloc, tokenLocal);

        List<CodeInstruction> body = new(instructions);
        if (body.Count == 0) {
            foreach (CodeInstruction inst in EmitEmptyBody(generator, sectionId, tokenLocal, returnType, hasReturn))
                yield return inst;
            yield break;
        }

        foreach (CodeInstruction inst in EmitGuardedBody(body, returnLocal, endLabel, hasReturn))
            yield return inst;
        foreach (CodeInstruction inst in EmitFinally(sectionId, tokenLocal))
            yield return inst;
        foreach (CodeInstruction inst in EmitEpilogue(returnLocal, endLabel, hasReturn))
            yield return inst;
    }

    // Nothing to guard, so stop the section and return a default value straight away.
    private static IEnumerable<CodeInstruction> EmitEmptyBody(
        ILGenerator generator, int sectionId, LocalBuilder tokenLocal, Type returnType, bool hasReturn
    ) {
        yield return new CodeInstruction(OpCodes.Ldc_I4, sectionId);
        yield return new CodeInstruction(OpCodes.Ldloc, tokenLocal);
        yield return new CodeInstruction(OpCodes.Call, s_StopByIdMethod);

        if (hasReturn) {
            if (returnType.IsValueType) {
                LocalBuilder defaultLocal = generator.DeclareLocal(returnType);
                yield return new CodeInstruction(OpCodes.Ldloca, defaultLocal);
                yield return new CodeInstruction(OpCodes.Initobj, returnType);
                yield return new CodeInstruction(OpCodes.Ldloc, defaultLocal);
            }
            else {
                yield return new CodeInstruction(OpCodes.Ldnull);
            }
        }

        yield return new CodeInstruction(OpCodes.Ret);
    }

    // The original body inside a try block. Every ret becomes a leave so the finally runs.
    private static IEnumerable<CodeInstruction> EmitGuardedBody(
        List<CodeInstruction> body, LocalBuilder? returnLocal, Label endLabel, bool hasReturn
    ) {
        body[0].blocks.Insert(0, new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));

        for (int i = 0; i < body.Count; i++) {
            CodeInstruction inst = body[i];
            if (inst.opcode != OpCodes.Ret) {
                yield return inst;
                continue;
            }

            if (hasReturn) {
                CodeInstruction stloc = new(OpCodes.Stloc, returnLocal!);
                stloc.labels.AddRange(inst.labels);
                stloc.blocks.AddRange(inst.blocks);
                yield return stloc;
                yield return new CodeInstruction(OpCodes.Leave, endLabel);
            }
            else {
                CodeInstruction leave = new(OpCodes.Leave, endLabel);
                leave.labels.AddRange(inst.labels);
                leave.blocks.AddRange(inst.blocks);
                yield return leave;
            }
        }
    }

    // Stop the section however the body exited. This is the PRD 11.6 exception-safety pair.
    private static IEnumerable<CodeInstruction> EmitFinally(int sectionId, LocalBuilder tokenLocal) {
        CodeInstruction finallyStart = new(OpCodes.Ldc_I4, sectionId);
        finallyStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock));
        yield return finallyStart;
        yield return new CodeInstruction(OpCodes.Ldloc, tokenLocal);
        yield return new CodeInstruction(OpCodes.Call, s_StopByIdMethod);

        CodeInstruction endFinally = new(OpCodes.Endfinally);
        endFinally.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        yield return endFinally;
    }

    private static IEnumerable<CodeInstruction> EmitEpilogue(
        LocalBuilder? returnLocal, Label endLabel, bool hasReturn
    ) {
        if (hasReturn) {
            CodeInstruction ldlocRet = new(OpCodes.Ldloc, returnLocal!);
            ldlocRet.labels.Add(endLabel);
            yield return ldlocRet;
            yield return new CodeInstruction(OpCodes.Ret);
        }
        else {
            CodeInstruction ret = new(OpCodes.Ret);
            ret.labels.Add(endLabel);
            yield return ret;
        }
    }
}

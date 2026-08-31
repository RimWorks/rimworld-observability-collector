using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using RimWorks.RimObs.Patching;

namespace RimWorks.RimObs.Config;

internal static class ProfilingXmlLoader {
    public const string FileName = "profiling.xml";

    public sealed class LoadResult {
        public int FilesScanned { get; internal set; }
        public int FilesLoaded { get; internal set; }
        public int SectionsDeclared { get; internal set; }
        public int MethodsDeclared { get; internal set; }
        public List<string> Warnings { get; } = new();
    }

    public static LoadResult LoadFromMods(IEnumerable<(string rootDir, string packageId)> mods) {
        if (mods == null)
            throw new ArgumentNullException(nameof(mods));

        LoadResult result = new();
        foreach ((string rootDir, string packageId) in mods) {
            if (string.IsNullOrEmpty(rootDir) || string.IsNullOrEmpty(packageId))
                continue;

            string profilingPath = Path.Combine(rootDir, "About", FileName);
            if (!File.Exists(profilingPath))
                continue;

            result.FilesScanned++;
            try {
                LoadFile(profilingPath, packageId, result);
                result.FilesLoaded++;
            }
            catch (Exception ex) {
                result.Warnings.Add($"[{packageId}] failed to parse {FileName}: {ex.Message}");
            }
        }
        return result;
    }

    internal static void LoadFile(string path, string packageId, LoadResult result) {
        XmlDocument doc = new();
        doc.Load(path);

        XmlElement? root = doc.DocumentElement;
        if (root == null || root.Name != "Profiling") {
            result.Warnings.Add($"[{packageId}] root element must be <Profiling>, got <{root?.Name ?? "null"}>");
            return;
        }

        foreach (XmlNode sectionNode in root.ChildNodes) {
            if (sectionNode.NodeType != XmlNodeType.Element || sectionNode.Name != "Section")
                continue;
            LoadSection((XmlElement)sectionNode, packageId, result);
        }
    }

    private static void LoadSection(XmlElement section, string packageId, LoadResult result) {
        string sectionName = section.GetAttribute("name");
        if (string.IsNullOrEmpty(sectionName)) {
            result.Warnings.Add($"[{packageId}] <Section> missing 'name' attribute");
            return;
        }

        string subsystem = section.GetAttribute("subsystem");
        string prefixedName = packageId + "." + sectionName;
        result.SectionsDeclared++;

        XmlElement? methodsContainer = null;
        foreach (XmlNode child in section.ChildNodes) {
            if (child.NodeType == XmlNodeType.Element && child.Name == "Methods") {
                methodsContainer = (XmlElement)child;
                break;
            }
        }

        if (methodsContainer == null) {
            result.Warnings.Add($"[{packageId}] section '{sectionName}' missing <Methods> container");
            return;
        }

        foreach (XmlNode methodNode in methodsContainer.ChildNodes) {
            if (methodNode.NodeType != XmlNodeType.Element || methodNode.Name != "Method")
                continue;
            LoadMethod(methodNode, sectionName, subsystem, prefixedName, packageId, result);
        }
    }

    private static void LoadMethod(XmlNode methodNode, string sectionName, string subsystem, string prefixedName, string packageId, LoadResult result) {
        string spec = methodNode.InnerText?.Trim() ?? string.Empty;
        if (spec.Length == 0) {
            result.Warnings.Add($"[{packageId}] section '{sectionName}' contains empty <Method>");
            return;
        }

        if (!TrySplitMethodSpec(spec, out string typeName, out string methodName)) {
            result.Warnings.Add($"[{packageId}] section '{sectionName}' invalid <Method> '{spec}' (expected 'Type.FullName:MethodName')");
            return;
        }

        string? entrySubsystem = string.IsNullOrEmpty(subsystem) ? null : subsystem;
        CatalogEntry entry = SectionCatalog.Register(prefixedName, typeName, methodName, null, entrySubsystem);
        entry.Declared = true;
        entry.Owner = packageId;
        result.MethodsDeclared++;
    }

    internal static bool TrySplitMethodSpec(string spec, out string typeName, out string methodName) {
        int colon = spec.LastIndexOf(':');
        if (colon <= 0 || colon >= spec.Length - 1) {
            typeName = string.Empty;
            methodName = string.Empty;
            return false;
        }

        typeName = spec.Substring(0, colon);
        methodName = spec.Substring(colon + 1);
        return typeName.Length > 0 && methodName.Length > 0;
    }
}

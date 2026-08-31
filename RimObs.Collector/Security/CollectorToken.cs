using System.Security.Cryptography;

namespace RimWorks.RimObs.Collector.Security;

public sealed class CollectorToken {
    public const string EnvVarName = "RIMOBS_TOKEN";
    private const int TokenByteLength = 32;

    private CollectorToken(string value, bool fromEnv) {
        Value = value;
        FromEnv = fromEnv;
    }

    public string Value { get; }
    public bool FromEnv { get; }

    public static CollectorToken CreateFromEnvOrGenerate() => CreateFromEnvOrGenerate(EnvVarName);

    public static CollectorToken CreateFromEnvOrGenerate(string envVarName) {
        string name = string.IsNullOrWhiteSpace(envVarName) ? EnvVarName : envVarName;
        string? envValue = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(envValue))
            return new CollectorToken(envValue, fromEnv: true);
        return new CollectorToken(Generate(), fromEnv: false);
    }

    public static CollectorToken FromExplicitValue(string value) {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Token value must not be empty.", nameof(value));
        return new CollectorToken(value, fromEnv: false);
    }

    public static string Generate() {
        byte[] buffer = new byte[TokenByteLength];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }

    public bool Matches(string? presented) {
        if (string.IsNullOrEmpty(presented))
            return false;
        byte[] a = System.Text.Encoding.UTF8.GetBytes(Value);
        byte[] b = System.Text.Encoding.UTF8.GetBytes(presented);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

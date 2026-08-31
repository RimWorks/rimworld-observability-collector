using System;

namespace RimWorks.RimObs.Api;

public static class NameValidator {
    public static void ValidateBareName(string name, string paramName) {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Name must not be empty.", paramName);

        char first = name[0];
        if (first < 'a' || first > 'z')
            throw new ArgumentException(
                $"Name '{name}' must start with a lowercase letter (a-z). Pattern: [a-z][a-z0-9_]*.",
                paramName
            );

        for (int i = 1; i < name.Length; i++) {
            char c = name[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (!ok)
                throw new ArgumentException(
                    $"Name '{name}' contains invalid character '{c}' at index {i}. Pattern: [a-z][a-z0-9_]*.",
                    paramName
                );
        }
    }
}

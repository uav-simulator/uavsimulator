using System;

namespace UavSimulator.Contracts
{
    [Serializable]
    public struct ContractVersion : IEquatable<ContractVersion>, IComparable<ContractVersion>
    {
        public int major;
        public int minor;
        public int patch;

        public ContractVersion(int major, int minor, int patch)
        {
            this.major = major;
            this.minor = minor;
            this.patch = patch;
        }

        public bool IsValid => major >= 0 && minor >= 0 && patch >= 0;

        public int CompareTo(ContractVersion other)
        {
            var majorCompare = major.CompareTo(other.major);
            if (majorCompare != 0) return majorCompare;

            var minorCompare = minor.CompareTo(other.minor);
            if (minorCompare != 0) return minorCompare;

            return patch.CompareTo(other.patch);
        }

        public bool Equals(ContractVersion other) =>
            major == other.major && minor == other.minor && patch == other.patch;

        public override bool Equals(object obj) => obj is ContractVersion other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(major, minor, patch);

        public override string ToString() => $"{major}.{minor}.{patch}";

        public static bool TryParse(string value, out ContractVersion version)
        {
            version = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var major)) return false;
            if (!int.TryParse(parts[1], out var minor)) return false;
            if (!int.TryParse(parts[2], out var patch)) return false;

            var parsed = new ContractVersion(major, minor, patch);
            if (!parsed.IsValid) return false;

            version = parsed;
            return true;
        }

        public static bool operator ==(ContractVersion left, ContractVersion right) => left.Equals(right);
        public static bool operator !=(ContractVersion left, ContractVersion right) => !left.Equals(right);
        public static bool operator <(ContractVersion left, ContractVersion right) => left.CompareTo(right) < 0;
        public static bool operator >(ContractVersion left, ContractVersion right) => left.CompareTo(right) > 0;
        public static bool operator <=(ContractVersion left, ContractVersion right) => left.CompareTo(right) <= 0;
        public static bool operator >=(ContractVersion left, ContractVersion right) => left.CompareTo(right) >= 0;
    }
}

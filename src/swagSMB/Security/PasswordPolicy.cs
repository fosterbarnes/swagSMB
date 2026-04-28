using System.Linq;

namespace swagSMB.Security
{
    public static class PasswordPolicy
    {
        public const int MasterMinimumLength = 12;
        public const int ShareMinimumLength = 8;

        // Kept for backward compat; equals MasterMinimumLength.
        public const int MinimumLength = MasterMinimumLength;

        public enum Strength
        {
            VeryWeak = 0,
            Weak = 1,
            Fair = 2,
            Strong = 3,
            VeryStrong = 4
        }

        public static Strength Estimate(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return Strength.VeryWeak;
            }

            int length = password.Length;
            bool hasLower = password.Any(char.IsLower);
            bool hasUpper = password.Any(char.IsUpper);
            bool hasDigit = password.Any(char.IsDigit);
            bool hasSymbol = password.Any(c => !char.IsLetterOrDigit(c));
            int classes = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);

            int score = 0;
            score += length >= 8 ? 1 : 0;
            score += length >= 12 ? 1 : 0;
            score += length >= 16 ? 1 : 0;
            score += classes >= 2 ? 1 : 0;
            score += classes >= 3 ? 1 : 0;
            score += classes >= 4 ? 1 : 0;

            return score switch
            {
                <= 1 => Strength.VeryWeak,
                2 => Strength.Weak,
                3 => Strength.Fair,
                4 => Strength.Strong,
                _ => Strength.VeryStrong
            };
        }

        public static string Describe(Strength s)
        {
            return s switch
            {
                Strength.VeryWeak => "Very weak",
                Strength.Weak => "Weak",
                Strength.Fair => "Fair",
                Strength.Strong => "Strong",
                _ => "Very strong"
            };
        }
    }
}

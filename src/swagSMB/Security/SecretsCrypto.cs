using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace swagSMB.Security
{
    public static class SecretsCrypto
    {
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const int HmacSize = 32;
        private const int KeySize = 32;
        private const int Iterations = 600_000;
        private const int MinAcceptedIterations = 100_000;
        private const int MaxAcceptedIterations = 2_000_000;
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("SWAGSMB1");

        public static byte[] Encrypt(string masterPassword, byte[] plaintext)
        {
            if (masterPassword == null)
            {
                throw new ArgumentNullException(nameof(masterPassword));
            }

            if (masterPassword.Length == 0)
            {
                throw new ArgumentException("Master password is required.", nameof(masterPassword));
            }

            byte[] passwordBytes = Encoding.UTF8.GetBytes(masterPassword);
            try
            {
                return Encrypt(passwordBytes, plaintext);
            }
            finally
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }

        public static byte[] Encrypt(byte[] masterPasswordBytes, byte[] plaintext)
        {
            if (masterPasswordBytes == null)
            {
                throw new ArgumentNullException(nameof(masterPasswordBytes));
            }

            if (masterPasswordBytes.Length == 0)
            {
                throw new ArgumentException("Master password is required.", nameof(masterPasswordBytes));
            }

            byte[] salt = RandomBytes(SaltSize);
            byte[] iv = RandomBytes(IvSize);

            byte[] keyMaterial = DeriveKey(masterPasswordBytes, salt, Iterations, KeySize * 2);
            byte[] encryptionKey = Slice(keyMaterial, 0, KeySize);
            byte[] macKey = Slice(keyMaterial, KeySize, KeySize);

            byte[] cipherText = EncryptAesCbc(encryptionKey, iv, plaintext);
            byte[] payloadWithoutMac = BuildPayloadWithoutMac(salt, iv, cipherText);
            byte[] mac = ComputeMac(macKey, payloadWithoutMac);

            byte[] payload = new byte[payloadWithoutMac.Length + mac.Length];
            Buffer.BlockCopy(payloadWithoutMac, 0, payload, 0, payloadWithoutMac.Length);
            Buffer.BlockCopy(mac, 0, payload, payloadWithoutMac.Length, mac.Length);
            Array.Clear(keyMaterial, 0, keyMaterial.Length);
            Array.Clear(encryptionKey, 0, encryptionKey.Length);
            Array.Clear(macKey, 0, macKey.Length);
            return payload;
        }

        public static byte[] Decrypt(string masterPassword, byte[] payload)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(masterPassword ?? string.Empty);
            try
            {
                return Decrypt(passwordBytes, payload);
            }
            finally
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }

        public static byte[] Decrypt(byte[] masterPasswordBytes, byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                throw new InvalidDataException("Encrypted payload is empty.");
            }

            int minLength = Header.Length + sizeof(int) + SaltSize + IvSize + HmacSize;
            if (payload.Length < minLength)
            {
                throw new InvalidDataException("Encrypted payload is invalid.");
            }

            int offset = 0;
            byte[] header = Slice(payload, offset, Header.Length);
            offset += Header.Length;
            if (!FixedEquals(header, Header))
            {
                throw new InvalidDataException("Invalid file header.");
            }

            int iterations = BitConverter.ToInt32(payload, offset);
            offset += sizeof(int);
            if (iterations < MinAcceptedIterations || iterations > MaxAcceptedIterations)
            {
                throw new InvalidDataException("Encrypted payload reports an unsupported iteration count.");
            }

            byte[] salt = Slice(payload, offset, SaltSize);
            offset += SaltSize;
            byte[] iv = Slice(payload, offset, IvSize);
            offset += IvSize;

            int cipherAndMacLength = payload.Length - offset;
            if (cipherAndMacLength <= HmacSize)
            {
                throw new InvalidDataException("Encrypted payload is incomplete.");
            }

            int cipherLength = cipherAndMacLength - HmacSize;
            byte[] cipherText = Slice(payload, offset, cipherLength);
            byte[] mac = Slice(payload, offset + cipherLength, HmacSize);

            byte[] keyMaterial = DeriveKey(masterPasswordBytes ?? Array.Empty<byte>(), salt, iterations, KeySize * 2);
            byte[] encryptionKey = Slice(keyMaterial, 0, KeySize);
            byte[] macKey = Slice(keyMaterial, KeySize, KeySize);

            try
            {
                // Encrypt-then-MAC: verify HMAC first, decrypt only after, to avoid padding-oracle behavior.
                byte[] computedMac = ComputeMac(macKey, payload, 0, payload.Length - HmacSize);
                if (!FixedEquals(mac, computedMac))
                {
                    throw new UnauthorizedAccessException("Invalid master password or corrupted file.");
                }

                return DecryptAesCbc(encryptionKey, iv, cipherText);
            }
            finally
            {
                Array.Clear(keyMaterial, 0, keyMaterial.Length);
                Array.Clear(encryptionKey, 0, encryptionKey.Length);
                Array.Clear(macKey, 0, macKey.Length);
            }
        }

        public static bool VerifyMasterPassword(string masterPassword, byte[] payload)
        {
            if (string.IsNullOrEmpty(masterPassword))
            {
                return false;
            }

            byte[] passwordBytes = Encoding.UTF8.GetBytes(masterPassword);
            try
            {
                return VerifyMasterPassword(passwordBytes, payload);
            }
            finally
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }

        public static bool VerifyMasterPassword(byte[] masterPasswordBytes, byte[] payload)
        {
            if (payload == null || payload.Length == 0 || masterPasswordBytes == null || masterPasswordBytes.Length == 0)
            {
                return false;
            }

            try
            {
                int minLength = Header.Length + sizeof(int) + SaltSize + IvSize + HmacSize;
                if (payload.Length < minLength)
                {
                    return false;
                }

                int offset = 0;
                byte[] header = Slice(payload, offset, Header.Length);
                offset += Header.Length;
                if (!FixedEquals(header, Header))
                {
                    return false;
                }

                int iterations = BitConverter.ToInt32(payload, offset);
                offset += sizeof(int);
                if (iterations < MinAcceptedIterations || iterations > MaxAcceptedIterations)
                {
                    return false;
                }

                byte[] salt = Slice(payload, offset, SaltSize);
                offset += SaltSize;
                offset += IvSize;

                int cipherAndMacLength = payload.Length - offset;
                if (cipherAndMacLength <= HmacSize)
                {
                    return false;
                }

                int macOffset = payload.Length - HmacSize;
                byte[] mac = Slice(payload, macOffset, HmacSize);
                byte[] keyMaterial = DeriveKey(masterPasswordBytes, salt, iterations, KeySize * 2);
                byte[] macKey = Slice(keyMaterial, KeySize, KeySize);
                try
                {
                    byte[] computedMac = ComputeMac(macKey, payload, 0, macOffset);
                    return FixedEquals(mac, computedMac);
                }
                finally
                {
                    Array.Clear(keyMaterial, 0, keyMaterial.Length);
                    Array.Clear(macKey, 0, macKey.Length);
                }
            }
            catch
            {
                return false;
            }
        }

        private static byte[] BuildPayloadWithoutMac(byte[] salt, byte[] iv, byte[] cipherText)
        {
            byte[] payload = new byte[Header.Length + sizeof(int) + salt.Length + iv.Length + cipherText.Length];
            int offset = 0;
            Buffer.BlockCopy(Header, 0, payload, offset, Header.Length);
            offset += Header.Length;
            Buffer.BlockCopy(BitConverter.GetBytes(Iterations), 0, payload, offset, sizeof(int));
            offset += sizeof(int);
            Buffer.BlockCopy(salt, 0, payload, offset, salt.Length);
            offset += salt.Length;
            Buffer.BlockCopy(iv, 0, payload, offset, iv.Length);
            offset += iv.Length;
            Buffer.BlockCopy(cipherText, 0, payload, offset, cipherText.Length);
            return payload;
        }

        private static byte[] DeriveKey(byte[] masterPasswordBytes, byte[] salt, int iterations, int length)
        {
            return Rfc2898DeriveBytes.Pbkdf2(masterPasswordBytes, salt, iterations, HashAlgorithmName.SHA256, length);
        }

        private static byte[] EncryptAesCbc(byte[] key, byte[] iv, byte[] plainText)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (var encryptor = aes.CreateEncryptor())
                {
                    return encryptor.TransformFinalBlock(plainText, 0, plainText.Length);
                }
            }
        }

        private static byte[] DecryptAesCbc(byte[] key, byte[] iv, byte[] cipherText)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (var decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
                }
            }
        }

        private static byte[] ComputeMac(byte[] macKey, byte[] payload)
        {
            using (var hmac = new HMACSHA256(macKey))
            {
                return hmac.ComputeHash(payload);
            }
        }

        private static byte[] ComputeMac(byte[] macKey, byte[] payload, int offset, int count)
        {
            using (var hmac = new HMACSHA256(macKey))
            {
                return hmac.ComputeHash(payload, offset, count);
            }
        }

        private static byte[] RandomBytes(int length)
        {
            byte[] buffer = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buffer);
            }
            return buffer;
        }

        private static bool FixedEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int diff = 0;
            for (int index = 0; index < left.Length; index++)
            {
                diff |= left[index] ^ right[index];
            }
            return diff == 0;
        }

        private static byte[] Slice(byte[] value, int offset, int length)
        {
            byte[] output = new byte[length];
            Buffer.BlockCopy(value, offset, output, 0, length);
            return output;
        }
    }
}

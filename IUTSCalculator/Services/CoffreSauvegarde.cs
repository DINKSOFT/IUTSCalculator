using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IUTSCalculator.Services
{
    /// <summary>Clés d'une sauvegarde, dérivées de son mot de passe (voir <see cref="CoffreSauvegarde.Deriver"/>).</summary>
    public sealed class ClesSauvegarde : IDisposable
    {
        internal ClesSauvegarde(byte[] materiau)
        {
            var sb = new StringBuilder(64);
            for (int i = 0; i < 32; i++) sb.Append(materiau[i].ToString("x2"));
            CleBases = sb.ToString();
            Chiffrement = new byte[32];
            Integrite = new byte[32];
            Buffer.BlockCopy(materiau, 32, Chiffrement, 0, 32);
            Buffer.BlockCopy(materiau, 64, Integrite, 0, 32);
            Array.Clear(materiau, 0, materiau.Length);
        }

        /// <summary>Clé des bases de la sauvegarde (hexadécimal, format SQLCipher).</summary>
        public string CleBases { get; private set; }

        internal byte[] Chiffrement { get; }
        internal byte[] Integrite { get; }

        public void Dispose()
        {
            Array.Clear(Chiffrement, 0, Chiffrement.Length);
            Array.Clear(Integrite, 0, Integrite.Length);
            CleBases = null;
        }
    }

    /// <summary>
    /// Chiffrement des sauvegardes portables par le mot de passe de sauvegarde.
    ///
    /// Le mot de passe donne, par PBKDF2-HMAC-SHA256 (sel aléatoire, nombre d'itérations noté dans
    /// le manifeste), trois clés indépendantes : celle des bases (SQLCipher), celle du coffre
    /// (AES-256-CBC) et celle de son contrôle d'intégrité (HMAC-SHA256, chiffrer puis authentifier).
    /// .NET Framework 4.8 n'a pas AES-GCM, d'où ce couple.
    ///
    /// Le coffre est un fichier : « SIGC1 » | IV (16 octets) | données chiffrées | HMAC (32 octets),
    /// le HMAC portant sur tout ce qui le précède. Il est vérifié en entier avant tout déchiffrement :
    /// un mauvais mot de passe ou une archive altérée sont refusés de la même façon.
    /// </summary>
    public static class CoffreSauvegarde
    {
        public const int IterationsParDefaut = 200000;
        public const int IterationsMin = 10000;
        public const int IterationsMax = 5000000;

        private static readonly byte[] Signature = Encoding.ASCII.GetBytes("SIGC1");
        private const int TailleIv = 16;
        private const int TailleMac = 32;

        public static byte[] NouveauSel()
        {
            var sel = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(sel);
            return sel;
        }

        public static ClesSauvegarde Deriver(string motDePasse, byte[] sel, int iterations)
        {
            if (string.IsNullOrEmpty(motDePasse)) throw new ArgumentException("Mot de passe de sauvegarde manquant.");
            if (sel == null || sel.Length < 16) throw new ArgumentException("Sel de sauvegarde invalide.");
            if (iterations < IterationsMin || iterations > IterationsMax) throw new ArgumentException("Nombre d'itérations de sauvegarde invalide.");
            using (var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(motDePasse), sel, iterations, HashAlgorithmName.SHA256))
                return new ClesSauvegarde(kdf.GetBytes(96));
        }

        public static void Chiffrer(string source, string cible, ClesSauvegarde cles)
        {
            using (var aes = Aes.Create())
            using (var mac = new HMACSHA256(cles.Integrite))
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = cles.Chiffrement;
                aes.GenerateIV();

                using (var sortie = new FileStream(cible, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    sortie.Write(Signature, 0, Signature.Length);
                    sortie.Write(aes.IV, 0, aes.IV.Length);
                    using (var entree = File.OpenRead(source))
                    using (var chiffreur = aes.CreateEncryptor())
                    using (var crypto = new CryptoStream(new NonFermant(sortie), chiffreur, CryptoStreamMode.Write))
                    {
                        entree.CopyTo(crypto);
                    }

                    sortie.Position = 0;
                    byte[] empreinte = Empreinte(mac, sortie, sortie.Length);
                    sortie.Seek(0, SeekOrigin.End);
                    sortie.Write(empreinte, 0, empreinte.Length);
                }
            }
        }

        /// <summary>
        /// Vérifie puis déchiffre le coffre. Lève <see cref="MotDePasseSauvegardeIncorrectException"/>
        /// si le contrôle d'intégrité échoue (mauvais mot de passe ou coffre altéré) ; rien n'est alors écrit.
        /// </summary>
        public static void Dechiffrer(string source, string cible, ClesSauvegarde cles)
        {
            using (var entree = File.OpenRead(source))
            {
                long longueur = entree.Length;
                if (longueur < Signature.Length + TailleIv + 16 + TailleMac)
                    throw new MotDePasseSauvegardeIncorrectException();

                var entete = new byte[Signature.Length + TailleIv];
                LireTout(entree, entete);
                for (int i = 0; i < Signature.Length; i++)
                    if (entete[i] != Signature[i]) throw new MotDePasseSauvegardeIncorrectException();

                byte[] attendue;
                using (var mac = new HMACSHA256(cles.Integrite))
                {
                    entree.Position = 0;
                    attendue = Empreinte(mac, entree, longueur - TailleMac);
                }
                var lue = new byte[TailleMac];
                entree.Position = longueur - TailleMac;
                LireTout(entree, lue);
                if (!EgalTempsConstant(attendue, lue))
                    throw new MotDePasseSauvegardeIncorrectException();

                var iv = new byte[TailleIv];
                Buffer.BlockCopy(entete, Signature.Length, iv, 0, TailleIv);
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = cles.Chiffrement;
                    aes.IV = iv;

                    entree.Position = entete.Length;
                    using (var portion = new Portion(entree, longueur - TailleMac - entete.Length))
                    using (var dechiffreur = aes.CreateDecryptor())
                    using (var crypto = new CryptoStream(portion, dechiffreur, CryptoStreamMode.Read))
                    using (var sortie = new FileStream(cible, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        crypto.CopyTo(sortie);
                    }
                }
            }
        }

        private static byte[] Empreinte(HMAC mac, Stream flux, long longueur)
        {
            var tampon = new byte[81920];
            long reste = longueur;
            while (reste > 0)
            {
                int n = flux.Read(tampon, 0, (int)Math.Min(tampon.Length, reste));
                if (n <= 0) throw new EndOfStreamException();
                mac.TransformBlock(tampon, 0, n, null, 0);
                reste -= n;
            }
            mac.TransformFinalBlock(new byte[0], 0, 0);
            return mac.Hash;
        }

        private static void LireTout(Stream flux, byte[] tampon)
        {
            int lu = 0;
            while (lu < tampon.Length)
            {
                int n = flux.Read(tampon, lu, tampon.Length - lu);
                if (n <= 0) throw new EndOfStreamException();
                lu += n;
            }
        }

        private static bool EgalTempsConstant(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>Flux d'écriture qui ne ferme pas le flux sous-jacent (le HMAC est calculé ensuite).</summary>
        private sealed class NonFermant : Stream
        {
            private readonly Stream _flux;
            public NonFermant(Stream flux) { _flux = flux; }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => _flux.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => _flux.Write(buffer, offset, count);
        }

        /// <summary>Lecture limitée aux <c>longueur</c> octets suivants du flux (les données chiffrées, sans le HMAC).</summary>
        private sealed class Portion : Stream
        {
            private readonly Stream _flux;
            private long _reste;
            public Portion(Stream flux, long longueur) { _flux = flux; _reste = longueur; }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_reste <= 0) return 0;
                int n = _flux.Read(buffer, offset, (int)Math.Min(count, _reste));
                _reste -= n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    /// <summary>Le mot de passe saisi n'ouvre pas la sauvegarde (ou celle-ci a été altérée).</summary>
    public class MotDePasseSauvegardeIncorrectException : InvalidOperationException
    {
        public MotDePasseSauvegardeIncorrectException()
            : base("Mot de passe de sauvegarde incorrect, ou sauvegarde altérée. Rien n'a été modifié.")
        {
        }
    }
}

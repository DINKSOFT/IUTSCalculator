using System;
using System.Security.Cryptography;
using System.Text;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service utilitaire transversal qui ajoute un numéro de contrôle unique — et, pour les
    /// documents PDF, un QR code correspondant — sur tous les documents générés par
    /// l'application (bulletins de paie et exports de calculs), afin de permettre à un
    /// employé ou à un tiers (banque, administration) de vérifier l'authenticité d'un
    /// document et de détecter une falsification.
    ///
    /// Le numéro de contrôle est calculé de façon déterministe à partir des données
    /// visibles sur le document (employé, période, montants, date de génération) : en cas
    /// de doute, il suffit de recalculer ce numéro à partir des mêmes données (conservées
    /// dans l'application) et de vérifier qu'il correspond à celui imprimé sur le document.
    /// Il ne s'agit toutefois pas d'une signature cryptographique inviolable (aucune clé
    /// secrète n'est utilisée) : c'est une protection dissuasive et un moyen simple de
    /// détecter une modification, pas une garantie absolue.
    /// </summary>
    public static class QrCodeService
    {
        /// <summary>
        /// Calcule un numéro de contrôle court et déterministe (format XXXX-XXXX-XXXX-XXXX)
        /// à partir du contenu fourni. Le même contenu produit toujours le même numéro.
        /// </summary>
        public static string GenererNumeroControle(string contenu)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(contenu ?? string.Empty));

                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                    sb.Append(hash[i].ToString("X2"));
                string hex = sb.ToString();

                return string.Join("-", new[]
                {
                    hex.Substring(0, 4),
                    hex.Substring(4, 4),
                    hex.Substring(8, 4),
                    hex.Substring(12, 4)
                });
            }
        }

        /// <summary>
        /// Génère l'image (au format iTextSharp, prête à insérer dans un PDF) du QR code
        /// correspondant au contenu de vérification fourni. Retourne null si la génération
        /// échoue (contenu trop long, erreur interne, etc.) : l'appelant doit alors se
        /// contenter du numéro de contrôle textuel.
        /// </summary>
        public static Image GenererImageQrCodePdf(string contenuVerification)
        {
            if (string.IsNullOrWhiteSpace(contenuVerification))
                return null;

            try
            {
                var qrCode = new BarcodeQRCode(contenuVerification, 1, 1, null);
                return qrCode.GetImage();
            }
            catch
            {
                // Contenu incompatible avec l'encodage QR ou erreur interne à iTextSharp :
                // on se rabat silencieusement sur le numéro de contrôle textuel seul.
                return null;
            }
        }

        /// <summary>
        /// Construit le texte de vérification encodé dans le QR code / associé au numéro de
        /// contrôle : suffisamment d'informations pour qu'un tiers puisse confronter le
        /// document à l'employeur, sans exposer de données sensibles superflues.
        /// </summary>
        public static string ConstruireContenuVerification(string typeDocument, string identifiant, string periodeOuDate, decimal montantNet, string numeroControle)
        {
            return $"Sigma Paie & RH — {typeDocument}\nRéf : {identifiant}\nPériode : {periodeOuDate}\nNet : {montantNet:N0} FCFA\nContrôle : {numeroControle}";
        }
    }
}

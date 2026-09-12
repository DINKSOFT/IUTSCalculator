using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Représente le dossier (l'entreprise) actuellement ouvert dans l'application.
    /// Toutes les vues/services de l'application travaillent sur ce dossier tant qu'un
    /// autre n'a pas été ouvert via l'écran de sélection de dossier.
    /// </summary>
    public class DossierSession
    {
        private static DossierSession _instance;
        public static DossierSession Instance => _instance ?? (_instance = new DossierSession());

        public Dossier CurrentDossier { get; private set; }

        public bool HasOpenDossier => CurrentDossier != null;

        public void Open(Dossier dossier) => CurrentDossier = dossier;

        public void Close() => CurrentDossier = null;
    }
}

using System;
using System.Globalization;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente une entrée dans le journal d'audit et de traçabilité (module ADM-02).
    ///
    /// Consigne chaque événement sensible en paie : qui a changé quel salaire, quand,
    /// et quelle était la valeur précédente.
    /// </summary>
    public class EntreeAudit
    {
        public long Id { get; set; }
        public DateTime DateAction { get; set; } = DateTime.Now;
        public string Utilisateur { get; set; }
        public string Action { get; set; }
        public string Entite { get; set; }
        public string EntiteId { get; set; }
        public string ChampModifie { get; set; }
        public string AncienneValeur { get; set; }
        public string NouvelleValeur { get; set; }
        public string Details { get; set; }
        public string AdresseMachine { get; set; }

        // ── Propriétés calculées pour l'affichage WPF et les rapports ─────────

        public string DateActionFormattee
        {
            get { return DateAction.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture); }
        }

        public string DateCourte
        {
            get { return DateAction.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture); }
        }

        public string Heure
        {
            get { return DateAction.ToString("HH:mm:ss", CultureInfo.InvariantCulture); }
        }

        public bool EstModificationFinanciere
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ChampModifie) && string.IsNullOrWhiteSpace(Action))
                    return false;

                string texte = ((ChampModifie ?? "") + " " + (Action ?? "")).ToLowerInvariant();
                return texte.Contains("salaire") ||
                       texte.Contains("indemnité") ||
                       texte.Contains("indemnite") ||
                       texte.Contains("montant") ||
                       texte.Contains("taux") ||
                       texte.Contains("prime") ||
                       texte.Contains("retenue") ||
                       texte.Contains("prêt") ||
                       texte.Contains("pret");
            }
        }

        public bool EstActionSensible
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Action)) return false;
                string act = Action.ToLowerInvariant();
                return act.Contains("suppression") ||
                       act.Contains("clôture") ||
                       act.Contains("cloture") ||
                       act.Contains("réouverture") ||
                       act.Contains("reouverture") ||
                       EstModificationFinanciere;
            }
        }

        public string TypeBadge
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Action)) return "Default";
                string act = Action.ToLowerInvariant();
                if (act.Contains("suppression")) return "Danger";
                if (act.Contains("clôture") || act.Contains("cloture")) return "Success";
                if (act.Contains("réouverture") || act.Contains("reouverture")) return "Warning";
                if (act.Contains("création") || act.Contains("creation")) return "Info";
                if (EstModificationFinanciere) return "Primary";
                return "Default";
            }
        }

        public string DescriptionCourte
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ChampModifie) &&
                    (!string.IsNullOrWhiteSpace(AncienneValeur) || !string.IsNullOrWhiteSpace(NouvelleValeur)))
                {
                    return string.Format("{0} : {1} → {2}", ChampModifie, AncienneValeur ?? "(vide)", NouvelleValeur ?? "(vide)");
                }

                return Details ?? Action;
            }
        }
    }

    /// <summary>
    /// Critères de recherche et de filtrage pour le journal d'audit.
    /// </summary>
    public class FiltreAudit
    {
        public DateTime? DateDebut { get; set; }
        public DateTime? DateFin { get; set; }
        public string Utilisateur { get; set; }
        public string Entite { get; set; }
        public string Action { get; set; }
        public string RechercheTexte { get; set; }
        public int Limite { get; set; } = 500;

        public bool EstActif
        {
            get
            {
                return DateDebut.HasValue ||
                       DateFin.HasValue ||
                       (!string.IsNullOrWhiteSpace(Utilisateur) && Utilisateur != "Tous les utilisateurs") ||
                       (!string.IsNullOrWhiteSpace(Entite) && Entite != "Toutes les entités") ||
                       (!string.IsNullOrWhiteSpace(Action) && Action != "Toutes les actions") ||
                       !string.IsNullOrWhiteSpace(RechercheTexte);
            }
        }
    }
}

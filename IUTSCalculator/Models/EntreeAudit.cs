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

        // ── Scellement (chaînage d'intégrité SHA-256, migration 38) ──────────

        /// <summary>
        /// Empreinte SHA-256 du contenu de l'entrée et de l'empreinte de l'entrée
        /// précédente. Vide pour les entrées antérieures au scellement du journal.
        /// </summary>
        public string Empreinte { get; set; }

        /// <summary>Empreinte de l'entrée précédente au moment de l'enregistrement.</summary>
        public string EmpreintePrecedente { get; set; }

        /// <summary>Marquée par « Vérifier l'intégrité » : contenu ou chaînage non conforme.</summary>
        public bool EstAlteree { get; set; }

        public bool EstScellee
        {
            get { return !string.IsNullOrEmpty(Empreinte); }
        }

        public string StatutScellement
        {
            get
            {
                if (EstAlteree) return "Altérée";
                return EstScellee ? "Scellée" : "Non scellée";
            }
        }

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

        /// <summary>
        /// Nombre maximal d'entrées renvoyées (taille d'une page à l'écran). 0 ou moins :
        /// aucune limite — c'est ce que demandent les exports, qui sortent tout le filtre.
        /// </summary>
        public int Limite { get; set; } = 500;

        /// <summary>Nombre d'entrées à sauter (pagination) : (page - 1) × Limite.</summary>
        public int Decalage { get; set; }

        /// <summary>
        /// N° de la dernière entrée prise en compte (figé quand on applique le filtre) : les
        /// entrées enregistrées ensuite ne décalent pas les pages déjà comptées. Vide : aucune borne.
        /// </summary>
        public long? IdMax { get; set; }

        /// <summary>Copie du filtre, pour paginer ou exporter sans toucher au filtre appliqué.</summary>
        public FiltreAudit Copier()
        {
            return new FiltreAudit
            {
                DateDebut = DateDebut,
                DateFin = DateFin,
                Utilisateur = Utilisateur,
                Entite = Entite,
                Action = Action,
                RechercheTexte = RechercheTexte,
                Limite = Limite,
                Decalage = Decalage,
                IdMax = IdMax
            };
        }

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

    /// <summary>
    /// Compteurs d'un filtre du journal d'audit, calculés sur TOUTES les entrées qui y
    /// répondent — et non sur la seule page affichée.
    /// </summary>
    public class StatistiquesAudit
    {
        public int Total { get; set; }
        public int ModificationsFinancieres { get; set; }
        public int ActionsSensibles { get; set; }
        public DateTime? DerniereActivite { get; set; }
    }

    /// <summary>
    /// Résultat de « Vérifier l'intégrité » : la chaîne des empreintes est recalculée
    /// depuis la première entrée scellée ; la première entrée non conforme est signalée.
    /// </summary>
    public class ResultatIntegriteAudit
    {
        public DateTime DateVerification { get; set; } = DateTime.Now;

        /// <summary>Faux si la base n'a pas encore les colonnes de scellement (migration 38).</summary>
        public bool ScellementActif { get; set; }

        public int EntreesControlees { get; set; }
        public int EntreesScellees { get; set; }
        public int EntreesNonScellees { get; set; }

        /// <summary>N° (Id) de la première entrée altérée, ou de l'entrée qui suit une rupture.</summary>
        public long? PremiereEntreeAlteree { get; set; }
        public DateTime? DatePremiereEntreeAlteree { get; set; }

        /// <summary>Description de la première anomalie ; null si la chaîne est intègre.</summary>
        public string Anomalie { get; set; }

        /// <summary>Empreinte de la dernière entrée conforme (« empreinte de tête »).</summary>
        public string EmpreinteDeTete { get; set; }

        public bool EstIntegre
        {
            get { return ScellementActif && Anomalie == null; }
        }

        public string Message
        {
            get
            {
                if (!ScellementActif)
                {
                    return "Journal non scellé : la base du dossier n'a pas encore reçu la mise à niveau " +
                           "qui ajoute les empreintes (schéma v38). Aucune vérification n'est possible.";
                }

                string avant = EntreesNonScellees > 0
                    ? string.Format(" {0} entrée(s) antérieure(s) au scellement sont marquées « non scellées » : elles ne peuvent pas être contrôlées.", EntreesNonScellees)
                    : "";

                if (Anomalie == null)
                {
                    return string.Format(
                        "✓ Intégrité vérifiée le {0:dd/MM/yyyy à HH:mm} : {1} entrée(s) scellée(s), toutes conformes.{2}{3}",
                        DateVerification, EntreesScellees, avant,
                        string.IsNullOrEmpty(EmpreinteDeTete) ? "" : " Empreinte de tête : " + EmpreinteDeTete + ".");
                }

                return string.Format(
                    "⚠ Journal altéré — {0} Les {1} entrée(s) scellée(s) qui précèdent sont conformes.{2}",
                    Anomalie, EntreesScellees, avant);
            }
        }
    }
}

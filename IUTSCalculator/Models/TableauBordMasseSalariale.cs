using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Tableau de bord masse salariale (module PIL-01).
    ///
    /// Restitue, pour une année civile, l'évolution mensuelle du brut, du net et du coût
    /// employeur, ainsi que leur ventilation par service et par catégorie — les deux axes
    /// que la feuille de route demande en complément de la tendance globale.
    ///
    /// Même principe de fabrication que <see cref="LivrePaie"/> et
    /// <see cref="EtatAnnuelSalaires"/> : ce n'est pas un recalcul, mais une lecture des
    /// bulletins déjà enregistrés sur les mois CLÔTURÉS de l'année choisie. Un mois ouvert
    /// n'apparaît pas dans l'évolution — ses totaux ne sont pas encore arrêtés — plutôt que
    /// d'afficher une tendance qui bougerait au recalcul.
    /// </summary>
    public class TableauBordMasseSalariale
    {
        // ── Identité du dossier (pour l'export) ─────────────────────────────
        public string RaisonSociale { get; set; }
        public string Ifu { get; set; }
        public string Rccm { get; set; }
        public string Adresse { get; set; }
        public string Ville { get; set; }
        public string Telephone { get; set; }

        public int Annee { get; set; }
        public DateTime DateGeneration { get; set; } = DateTime.Now;

        /// <summary>Mois (1-12) de l'année dont la période est clôturée et donc restituée.</summary>
        public List<int> MoisClotures { get; set; } = new List<int>();

        /// <summary>Mois (1-12) encore ouverts ou jamais créés — absents de l'évolution.</summary>
        public List<int> MoisManquants { get; set; } = new List<int>();

        /// <summary>Vrai lorsque les douze mois de l'année sont clôturés.</summary>
        public bool EstComplet => MoisManquants.Count == 0 && MoisClotures.Count == 12;

        /// <summary>Un point par mois clôturé, dans l'ordre chronologique.</summary>
        public List<PointEvolutionMensuelle> Evolution { get; set; } = new List<PointEvolutionMensuelle>();

        /// <summary>Cumuls de l'année, ventilés par catégorie de salarié.</summary>
        public List<VentilationMasseSalariale> ParCategorie { get; set; } = new List<VentilationMasseSalariale>();

        /// <summary>Cumuls de l'année, ventilés par service / département.</summary>
        public List<VentilationMasseSalariale> ParService { get; set; } = new List<VentilationMasseSalariale>();

        public decimal TotalBrut => Evolution.Sum(p => p.Brut);
        public decimal TotalNet => Evolution.Sum(p => p.Net);
        public decimal TotalCoutEmployeur => Evolution.Sum(p => p.CoutEmployeur);
        public int TotalBulletins => Evolution.Sum(p => p.NombreBulletins);

        /// <summary>Moyenne mensuelle du coût employeur sur les mois clôturés — 0 si aucun.</summary>
        public decimal MoyenneMensuelleCoutEmployeur =>
            Evolution.Count == 0 ? 0m : TotalCoutEmployeur / Evolution.Count;

        public string PeriodeLibelle => string.Format(CultureInfo.InvariantCulture, "Année {0}", Annee);

        public string NomFichierPropose =>
            string.Format(CultureInfo.InvariantCulture, "Tableau_bord_masse_salariale_{0}.xlsx", Annee);

        private static string NomMois(int mois)
        {
            var culture = CultureInfo.GetCultureInfo("fr-FR");
            string nom = culture.DateTimeFormat.GetMonthName(mois);
            return nom.Length > 0 ? char.ToUpper(nom[0], culture) + nom.Substring(1) : nom;
        }

        public string MoisClosLibelle =>
            MoisClotures.Count == 0 ? "aucun" : string.Join(", ", MoisClotures.OrderBy(m => m).Select(NomMois));

        public string MoisManquantsLibelle =>
            MoisManquants.Count == 0 ? "aucun" : string.Join(", ", MoisManquants.OrderBy(m => m).Select(NomMois));

        /// <summary>
        /// Avertissements affichés à l'écran — même convention que les autres états
        /// (<see cref="LivrePaie.Anomalies"/>, <see cref="EtatAnnuelSalaires.Anomalies"/>).
        /// </summary>
        public IEnumerable<string> Anomalies()
        {
            if (Evolution.Count == 0)
            {
                yield return "Aucun mois clôturé sur cette année : rien à afficher. Clôturez les périodes de paie (module PAI-01) pour alimenter le tableau de bord.";
                yield break;
            }

            if (!EstComplet)
            {
                yield return string.Format(
                    "Année partielle : {0} mois clôturé(s) sur 12 ({1}). Mois manquants : {2}.",
                    MoisClotures.Count, MoisClosLibelle, MoisManquantsLibelle);
            }

            if (ParService.Any(v => string.IsNullOrWhiteSpace(v.Cle) || v.Cle == "Non renseigné"))
            {
                yield return "Un ou plusieurs salariés n'ont pas de service renseigné sur leur fiche : leurs montants apparaissent sous « Non renseigné ». Complétez la fiche salarié (écran Gestion des employés) pour affiner la ventilation.";
            }
        }
    }

    /// <summary>Un mois de l'évolution : les trois métriques suivies, plus leur hauteur de barre précalculée.</summary>
    public class PointEvolutionMensuelle
    {
        public int Mois { get; set; }
        public string Designation { get; set; }
        public decimal Brut { get; set; }
        public decimal Net { get; set; }
        public decimal CoutEmployeur { get; set; }
        public int NombreBulletins { get; set; }

        /// <summary>
        /// Hauteurs en pixels des trois barres du mois, précalculées par le service qui
        /// connaît le maximum de la série (voir <see cref="TableauBordMasseSalarialeService"/>).
        /// Un point WPF direct : la vue n'a besoin d'aucun convertisseur pour l'afficher.
        /// </summary>
        public double HauteurBrutPx { get; set; }
        public double HauteurNetPx { get; set; }
        public double HauteurCoutEmployeurPx { get; set; }
    }

    /// <summary>Un cumul annuel pour une valeur de l'axe de ventilation (catégorie ou service).</summary>
    public class VentilationMasseSalariale
    {
        /// <summary>Libellé de la catégorie ou du service ; « Non renseigné » si la fiche est vide.</summary>
        public string Cle { get; set; }
        public int NombreBulletins { get; set; }
        public decimal Brut { get; set; }
        public decimal Net { get; set; }
        public decimal CoutEmployeur { get; set; }

        /// <summary>Part du coût employeur de cette ligne dans le total (0 à 100).</summary>
        public double PartPourcentage { get; set; }

        /// <summary>Largeur en pixels de la barre proportionnelle affichée à l'écran.</summary>
        public double LargeurBarrePx { get; set; }
    }
}

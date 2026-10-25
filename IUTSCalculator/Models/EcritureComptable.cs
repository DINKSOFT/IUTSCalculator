using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente la pièce comptable mensuelle de paie (OD de paie)
    /// selon le plan comptable SYSCOHADA révisé (module CPT-01).
    /// </summary>
    public class PieceComptable
    {
        // ── En-tête de la pièce ──────────────────────────────────────────────

        /// <summary>N° de pièce (EC_Piece de Sage), construit depuis les paramètres comptables du dossier.</summary>
        public string NumeroPiece { get; set; }

        /// <summary>Référence de la pièce (EC_Reference de Sage).</summary>
        public string Reference { get; set; }

        public string CodeJournal { get; set; } = ParametresComptables.CodeJournalParDefaut;
        public string LibelleJournal { get; set; } = ParametresComptables.LibelleJournalParDefaut;
        public DateTime DatePiece { get; set; }

        public string RaisonSociale { get; set; }
        public string Ifu { get; set; }

        // ── Période couverte ─────────────────────────────────────────────────

        public DateTime DebutPeriode { get; set; }
        public DateTime FinPeriode { get; set; }

        /// <summary>Vrai si la période n'est pas encore clôturée dans les périodes de paie.</summary>
        public bool EstBrouillon { get; set; }

        public DateTime DateGeneration { get; set; } = DateTime.Now;

        // ── Comptes généraux ─────────────────────────────────────────────────

        /// <summary>Longueur appliquée aux comptes généraux de la pièce (complétés par des zéros à droite).</summary>
        public int LongueurComptes { get; set; }

        /// <summary>Vrai si la longueur a été déduite du plus long compte utilisé (paramètre à 0).</summary>
        public bool LongueurComptesAutomatique { get; set; }

        /// <summary>Comptes plus longs que la longueur paramétrée, ou autrement suspects.</summary>
        public List<string> AvertissementsComptes { get; set; } = new List<string>();

        /// <summary>Le fichier d'import Sage commence par la ligne des noms de colonnes.</summary>
        public bool EnTeteFichierSage { get; set; } = true;

        // ── Lignes d'écriture ────────────────────────────────────────────────

        public List<LigneEcritureComptable> Lignes { get; set; } = new List<LigneEcritureComptable>();

        // ── Totaux et équilibre ──────────────────────────────────────────────

        public decimal TotalDebit
        {
            get { return Lignes.Sum(l => l.Debit); }
        }

        public decimal TotalCredit
        {
            get { return Lignes.Sum(l => l.Credit); }
        }

        public decimal Ecart
        {
            get { return TotalDebit - TotalCredit; }
        }

        public bool EstEquilibree
        {
            get { return Math.Abs(Ecart) < 0.005m; }
        }

        public int NombreLignes
        {
            get { return Lignes.Count; }
        }

        // ── Libellés et export ───────────────────────────────────────────────

        public string PeriodeLibelle
        {
            get
            {
                var culture = CultureInfo.GetCultureInfo("fr-FR");
                string texte = FinPeriode.ToString("MMMM yyyy", culture);
                return culture.TextInfo.ToTitleCase(texte);
            }
        }

        /// <summary>Période sous forme courte (MM/AAAA), utilisée dans les intitulés Sage.</summary>
        public string PeriodeCourte
        {
            get { return FinPeriode.ToString("MM/yyyy", CultureInfo.InvariantCulture); }
        }

        /// <summary>En-tête du tableau à l'écran : « JOURNAL PAIE — JOURNAL DE PAIE ».</summary>
        public string EnteteJournal
        {
            get
            {
                string libelle = string.IsNullOrWhiteSpace(LibelleJournal) ? "" : " — " + LibelleJournal;
                return ("Journal " + (CodeJournal ?? "") + libelle).ToUpper(CultureInfo.GetCultureInfo("fr-FR"));
            }
        }

        public string LongueurComptesLibelle
        {
            get
            {
                return string.Format("Comptes généraux sur {0} caractères ({1})",
                    LongueurComptes, LongueurComptesAutomatique ? "longueur automatique" : "longueur paramétrée");
            }
        }

        public string NomFichierExcel
        {
            get
            {
                string entreprise = (RaisonSociale ?? "Entreprise").Replace(" ", "_");
                return string.Format("OD_Paie_{0}_{1:yyyy_MM}.xlsx", entreprise, FinPeriode);
            }
        }

        public string NomFichierTxt
        {
            get
            {
                string entreprise = (RaisonSociale ?? "Entreprise").Replace(" ", "_");
                return string.Format("OD_Paie_{0}_{1:yyyy_MM}.txt", entreprise, FinPeriode);
            }
        }

        public string NomFichierSage
        {
            get
            {
                string entreprise = (RaisonSociale ?? "Entreprise").Replace(" ", "_");
                return string.Format("Import_Sage_Paie_{0}_{1:yyyy_MM}.txt", entreprise, FinPeriode);
            }
        }

        public List<string> Anomalies()
        {
            var liste = new List<string>();

            if (EstBrouillon)
            {
                liste.Add(string.Format(
                    "La période {0} n'est pas encore clôturée : cette écriture est un brouillon.",
                    PeriodeLibelle));
            }

            if (Lignes.Count == 0)
            {
                liste.Add("Aucune écriture générée : aucun bulletin enregistré sur cette période.");
            }
            else if (!EstEquilibree)
            {
                liste.Add(string.Format(
                    "Déséquilibre comptable détecté : Total Débit ({0:N0}) != Total Crédit ({1:N0}), Écart : {2:N0} FCFA.",
                    TotalDebit, TotalCredit, Ecart));
            }

            if (AvertissementsComptes != null)
                liste.AddRange(AvertissementsComptes);

            return liste;
        }
    }

    /// <summary>
    /// Une ligne d'écriture comptable dans la pièce d'OD de paie.
    /// </summary>
    public class LigneEcritureComptable
    {
        public int Ordre { get; set; }
        public DateTime DateEcriture { get; set; }
        public string CodeJournal { get; set; } = ParametresComptables.CodeJournalParDefaut;
        public string ReferencePiece { get; set; }
        public string CompteGeneral { get; set; }
        public string IntituleCompte { get; set; }
        public string LibelleOperation { get; set; }

        /// <summary>
        /// Intitulé court destiné à Sage (EC_Intitule) : moins de 34 caractères, période
        /// comprise. Le libellé d'opération complet ne tient pas dans cette limite.
        /// </summary>
        public string LibelleSage { get; set; }

        /// <summary>Compte tiers (CT_Num de Sage) — vide : la pièce de paie est centralisée.</summary>
        public string CompteTiers { get; set; }

        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string CodeAnalytique { get; set; }
        public string Matricule { get; set; }

        public bool EstDebit
        {
            get { return Debit > 0m; }
        }

        public bool EstCredit
        {
            get { return Credit > 0m; }
        }
    }

    /// <summary>
    /// Une ligne du fichier d'import Sage 100c Comptabilité, au format déjà utilisé par le
    /// client dans ses autres travaux :
    /// JO_Num | EC_Reference | Date | EC_Piece | CG_Num | CT_Num | EC_Intitule | Sens | Montant.
    /// </summary>
    public class LigneImportSage
    {
        public string JO_Num { get; set; }
        public string EC_Reference { get; set; }

        /// <summary>Date en texte JJ/MM/AAAA (jamais une date sérialisée).</summary>
        public string Date { get; set; }

        public string EC_Piece { get; set; }

        /// <summary>Compte général en texte : les zéros sont conservés.</summary>
        public string CG_Num { get; set; }

        public string CT_Num { get; set; }
        public string EC_Intitule { get; set; }

        /// <summary>« D » (débit) ou « C » (crédit).</summary>
        public string Sens { get; set; }

        /// <summary>Montant unique, toujours positif.</summary>
        public decimal Montant { get; set; }

        public const string SensDebit = "D";
        public const string SensCredit = "C";

        public static readonly string[] Colonnes =
        {
            "JO_Num", "EC_Reference", "Date", "EC_Piece", "CG_Num", "CT_Num", "EC_Intitule", "Sens", "Montant"
        };

        /// <summary>
        /// Ramène un intitulé sous la limite de Sage (33 caractères au plus, la limite
        /// étant « strictement inférieur à 34 »). La coupe recule jusqu'au dernier espace
        /// quand elle tomberait au milieu d'un mot, et ne laisse ni espace ni ponctuation
        /// pendante en fin d'intitulé. Le point-virgule, séparateur du fichier, est remplacé.
        /// </summary>
        public static string TronquerIntitule(string texte, int longueurMax = ParametresComptables.LongueurMaxIntituleSage)
        {
            string propre = Regex.Replace((texte ?? "").Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), " {2,}", " ").Trim();
            if (longueurMax <= 0) return "";
            if (propre.Length <= longueurMax) return propre;

            string coupe = propre.Substring(0, longueurMax);
            if (!char.IsWhiteSpace(propre[longueurMax]))
            {
                int espace = coupe.LastIndexOf(' ');
                if (espace >= (longueurMax * 2) / 3)
                    coupe = coupe.Substring(0, espace);
            }

            return coupe.TrimEnd(' ', '-', '—', ',', '.', ';', ':', '(', '/', '+', '\'');
        }

        /// <summary>
        /// Intitulé « libellé + suffixe » (la période, en général) sous la limite de Sage :
        /// c'est le libellé qui est raccourci, jamais le suffixe.
        /// </summary>
        public static string IntituleAvecSuffixe(string libelle, string suffixe, int longueurMax = ParametresComptables.LongueurMaxIntituleSage)
        {
            string fin = TronquerIntitule(suffixe, longueurMax);
            if (string.IsNullOrEmpty(fin))
                return TronquerIntitule(libelle, longueurMax);

            int place = longueurMax - fin.Length - 1;
            string tete = place > 0 ? TronquerIntitule(libelle, place) : "";
            return string.IsNullOrEmpty(tete) ? fin : tete + " " + fin;
        }
    }

    /// <summary>
    /// Définition d'un paramètre comptable du dossier : clé de stockage, libellé d'écran
    /// et valeur initiale.
    /// </summary>
    public sealed class DefinitionParametreComptable
    {
        public DefinitionParametreComptable(string cle, string libelle, string valeurParDefaut, bool estCompte)
        {
            Cle = cle;
            Libelle = libelle;
            ValeurParDefaut = valeurParDefaut;
            EstCompte = estCompte;
        }

        public string Cle { get; private set; }
        public string Libelle { get; private set; }
        public string ValeurParDefaut { get; private set; }
        public bool EstCompte { get; private set; }
    }

    /// <summary>
    /// Paramètres comptables de la pièce de paie, propres à chaque dossier (défaut 20 du
    /// guide de formation 1.4.37.1).
    ///
    /// Les comptes de charges patronales et de dettes (CNSS, IUTS, TPA, retenue spéciale,
    /// net à payer…) étaient écrits en dur dans EcriturePaieService : impossible de les
    /// aligner sur le plan comptable du client. Ils deviennent des paramètres stockés dans
    /// la table ParametresComptables (Cle, Valeur) de la base du dossier. Leurs valeurs
    /// initiales sont EXACTEMENT celles qui étaient codées en dur : tant que l'utilisateur
    /// ne les modifie pas, l'écriture produite garde les mêmes comptes.
    ///
    /// Les comptes portés par les rubriques du Plan de paie restent prioritaires pour les
    /// salaires, indemnités, gains et retenues variables ; les comptes « de repli »
    /// ci-dessous ne servent qu'aux rubriques sans compte.
    /// </summary>
    public class ParametresComptables : INotifyPropertyChanged
    {
        // ── Limites du format d'import Sage 100c Comptabilité ────────────────

        public const int LongueurMaxCompteSage = 13;
        public const int LongueurMaxJournalSage = 6;
        public const int LongueurMaxPieceSage = 13;
        public const int LongueurMaxReferenceSage = 17;

        /// <summary>EC_Intitule doit faire strictement moins de 34 caractères.</summary>
        public const int LongueurMaxIntituleSage = 33;

        public const string CodeJournalParDefaut = "PAIE";
        public const string LibelleJournalParDefaut = "Journal de paie";

        // ── Clés de stockage ─────────────────────────────────────────────────

        public const string CleCodeJournal = "CodeJournal";
        public const string CleLibelleJournal = "LibelleJournal";
        public const string CleFormatPiece = "FormatPiece";
        public const string CleFormatReference = "FormatReference";
        public const string CleLongueurComptes = "LongueurComptes";
        public const string CleEnTeteFichierSage = "EnTeteFichierSage";

        public const string CleCompteSalaireBase = "CompteSalaireBase";
        public const string CleCompteIndemniteLogement = "CompteIndemniteLogement";
        public const string CleCompteIndemniteTransport = "CompteIndemniteTransport";
        public const string CleCompteIndemniteFonction = "CompteIndemniteFonction";
        public const string CleCompteIndemniteSujetion = "CompteIndemniteSujetion";
        public const string CleCompteGainsVariables = "CompteGainsVariables";
        public const string CleCompteChargesCnss = "CompteChargesCnss";
        public const string CleCompteChargeTpa = "CompteChargeTpa";
        public const string CleCompteCnss = "CompteCnss";
        public const string CleCompteIuts = "CompteIuts";
        public const string CleCompteTpa = "CompteTpa";
        public const string CleCompteRetenueSpeciale = "CompteRetenueSpeciale";
        public const string CleCompteRetenuesDiverses = "CompteRetenuesDiverses";
        public const string CleCompteNetAPayer = "CompteNetAPayer";

        /// <summary>
        /// Tous les paramètres, dans l'ordre de l'écran. Les valeurs initiales des comptes
        /// sont celles que le programme écrivait en dur jusqu'à la version 1.4.37.1.
        /// </summary>
        private static readonly List<DefinitionParametreComptable> _definitions = new List<DefinitionParametreComptable>
        {
            new DefinitionParametreComptable(CleCodeJournal, "Code journal (JO_Num)", CodeJournalParDefaut, false),
            new DefinitionParametreComptable(CleLibelleJournal, "Libellé du journal", LibelleJournalParDefaut, false),
            new DefinitionParametreComptable(CleFormatPiece, "N° de pièce (EC_Piece)", "PAIE-{AAAA}{MM}", false),
            new DefinitionParametreComptable(CleFormatReference, "Référence (EC_Reference)", "PAIE-{AAAA}{MM}", false),
            new DefinitionParametreComptable(CleLongueurComptes, "Longueur des comptes généraux", "0", false),
            new DefinitionParametreComptable(CleEnTeteFichierSage, "Ligne d'en-tête du fichier Sage", "1", false),

            new DefinitionParametreComptable(CleCompteChargesCnss, "Charges sociales patronales CNSS", "664100", true),
            new DefinitionParametreComptable(CleCompteChargeTpa, "Taxe patronale et d'apprentissage (charge)", "641300", true),
            new DefinitionParametreComptable(CleCompteSalaireBase, "Salaire de base (repli)", "661100", true),
            new DefinitionParametreComptable(CleCompteIndemniteLogement, "Indemnité de logement (repli)", "663100", true),
            new DefinitionParametreComptable(CleCompteIndemniteTransport, "Indemnité de transport (repli)", "663200", true),
            new DefinitionParametreComptable(CleCompteIndemniteFonction, "Indemnité de fonction (repli)", "663300", true),
            new DefinitionParametreComptable(CleCompteIndemniteSujetion, "Indemnité de sujétion (repli)", "663400", true),
            new DefinitionParametreComptable(CleCompteGainsVariables, "Gains variables (repli)", "661800", true),

            new DefinitionParametreComptable(CleCompteCnss, "CNSS à payer (parts salariale et patronale)", "431100", true),
            new DefinitionParametreComptable(CleCompteIuts, "IUTS retenu à reverser", "447200", true),
            new DefinitionParametreComptable(CleCompteTpa, "TPA à payer", "447300", true),
            new DefinitionParametreComptable(CleCompteRetenueSpeciale, "Retenue spéciale à reverser", "447800", true),
            new DefinitionParametreComptable(CleCompteNetAPayer, "Net à payer (rémunérations dues)", "422000", true),
            new DefinitionParametreComptable(CleCompteRetenuesDiverses, "Retenues diverses (repli)", "422100", true)
        };

        public static IList<DefinitionParametreComptable> Definitions
        {
            get { return _definitions.AsReadOnly(); }
        }

        /// <summary>Valeurs initiales de tous les paramètres (clé → valeur), pour la migration et le rétablissement.</summary>
        public static Dictionary<string, string> ValeursInitiales()
        {
            var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _definitions)
                valeurs[d.Cle] = d.ValeurParDefaut;
            return valeurs;
        }

        public static DefinitionParametreComptable Definition(string cle)
        {
            return _definitions.FirstOrDefault(d => string.Equals(d.Cle, cle, StringComparison.OrdinalIgnoreCase));
        }

        private readonly Dictionary<string, string> _valeurs;

        public ParametresComptables()
        {
            _valeurs = ValeursInitiales();
        }

        /// <summary>
        /// Paramètres lus en base : une clé absente (ou vide pour un compte) garde sa valeur
        /// initiale, une clé inconnue est ignorée.
        /// </summary>
        public static ParametresComptables Depuis(IDictionary<string, string> valeurs)
        {
            var p = new ParametresComptables();
            if (valeurs == null) return p;

            foreach (var d in _definitions)
            {
                string v;
                if (!valeurs.TryGetValue(d.Cle, out v) || v == null) continue;
                if (d.EstCompte && string.IsNullOrWhiteSpace(v)) continue;
                p._valeurs[d.Cle] = v;
            }
            return p;
        }

        public ParametresComptables Clone()
        {
            return Depuis(_valeurs);
        }

        /// <summary>
        /// Remet chaque paramètre à sa valeur initiale (celle d'avant le paramétrage). Le nom
        /// de chaque propriété est celui de sa clé : l'écran est prévenu champ par champ.
        /// </summary>
        public void RetablirValeursInitiales()
        {
            foreach (var d in _definitions)
            {
                if (string.Equals(ValeurDe(d.Cle), d.ValeurParDefaut, StringComparison.Ordinal)) continue;
                _valeurs[d.Cle] = d.ValeurParDefaut;
                OnPropertyChanged(d.Cle);
            }
        }

        /// <summary>Copie des valeurs (clé → valeur), dans l'ordre des définitions.</summary>
        public Dictionary<string, string> Valeurs()
        {
            var copie = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _definitions)
                copie[d.Cle] = ValeurDe(d.Cle);
            return copie;
        }

        public string ValeurDe(string cle)
        {
            string v;
            return _valeurs.TryGetValue(cle, out v) ? (v ?? "") : "";
        }

        private string Lire(string cle)
        {
            return ValeurDe(cle);
        }

        private void Ecrire(string cle, string valeur, [CallerMemberName] string propriete = null)
        {
            string nouvelle = valeur ?? "";
            if (string.Equals(ValeurDe(cle), nouvelle, StringComparison.Ordinal)) return;
            _valeurs[cle] = nouvelle;
            OnPropertyChanged(propriete);
        }

        // ── Journal et pièce ─────────────────────────────────────────────────

        public string CodeJournal
        {
            get { return Lire(CleCodeJournal); }
            set { Ecrire(CleCodeJournal, value); }
        }

        public string LibelleJournal
        {
            get { return Lire(CleLibelleJournal); }
            set { Ecrire(CleLibelleJournal, value); }
        }

        /// <summary>Modèle du n° de pièce : {AAAA} année, {AA} année sur deux chiffres, {MM} mois.</summary>
        public string FormatPiece
        {
            get { return Lire(CleFormatPiece); }
            set { Ecrire(CleFormatPiece, value); }
        }

        public string FormatReference
        {
            get { return Lire(CleFormatReference); }
            set { Ecrire(CleFormatReference, value); }
        }

        /// <summary>
        /// Longueur des comptes généraux. 0 : automatique, c'est-à-dire la longueur du plus
        /// long compte utilisé (rubriques du Plan de paie et paramètres ci-dessous).
        /// </summary>
        public int LongueurComptes
        {
            get
            {
                int n;
                return int.TryParse(Lire(CleLongueurComptes), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
            }
            set { Ecrire(CleLongueurComptes, value.ToString(CultureInfo.InvariantCulture)); }
        }

        public bool EnTeteFichierSage
        {
            get { return Lire(CleEnTeteFichierSage) != "0"; }
            set { Ecrire(CleEnTeteFichierSage, value ? "1" : "0"); }
        }

        // ── Comptes de charges (débit) ───────────────────────────────────────

        public string CompteChargesCnss
        {
            get { return Lire(CleCompteChargesCnss); }
            set { Ecrire(CleCompteChargesCnss, value); }
        }

        public string CompteChargeTpa
        {
            get { return Lire(CleCompteChargeTpa); }
            set { Ecrire(CleCompteChargeTpa, value); }
        }

        public string CompteSalaireBase
        {
            get { return Lire(CleCompteSalaireBase); }
            set { Ecrire(CleCompteSalaireBase, value); }
        }

        public string CompteIndemniteLogement
        {
            get { return Lire(CleCompteIndemniteLogement); }
            set { Ecrire(CleCompteIndemniteLogement, value); }
        }

        public string CompteIndemniteTransport
        {
            get { return Lire(CleCompteIndemniteTransport); }
            set { Ecrire(CleCompteIndemniteTransport, value); }
        }

        public string CompteIndemniteFonction
        {
            get { return Lire(CleCompteIndemniteFonction); }
            set { Ecrire(CleCompteIndemniteFonction, value); }
        }

        public string CompteIndemniteSujetion
        {
            get { return Lire(CleCompteIndemniteSujetion); }
            set { Ecrire(CleCompteIndemniteSujetion, value); }
        }

        public string CompteGainsVariables
        {
            get { return Lire(CleCompteGainsVariables); }
            set { Ecrire(CleCompteGainsVariables, value); }
        }

        // ── Comptes de tiers (crédit) ────────────────────────────────────────

        public string CompteCnss
        {
            get { return Lire(CleCompteCnss); }
            set { Ecrire(CleCompteCnss, value); }
        }

        public string CompteIuts
        {
            get { return Lire(CleCompteIuts); }
            set { Ecrire(CleCompteIuts, value); }
        }

        public string CompteTpa
        {
            get { return Lire(CleCompteTpa); }
            set { Ecrire(CleCompteTpa, value); }
        }

        public string CompteRetenueSpeciale
        {
            get { return Lire(CleCompteRetenueSpeciale); }
            set { Ecrire(CleCompteRetenueSpeciale, value); }
        }

        public string CompteNetAPayer
        {
            get { return Lire(CleCompteNetAPayer); }
            set { Ecrire(CleCompteNetAPayer, value); }
        }

        public string CompteRetenuesDiverses
        {
            get { return Lire(CleCompteRetenuesDiverses); }
            set { Ecrire(CleCompteRetenuesDiverses, value); }
        }

        // ── Calculs ──────────────────────────────────────────────────────────

        /// <summary>Tous les comptes paramétrés (nettoyés), pour le calcul de la longueur automatique.</summary>
        public IEnumerable<string> Comptes()
        {
            return _definitions.Where(d => d.EstCompte).Select(d => NettoyerCompte(ValeurDe(d.Cle)));
        }

        public string NumeroPiece(DateTime finPeriode)
        {
            return AppliquerFormat(FormatPiece, finPeriode);
        }

        public string Reference(DateTime finPeriode)
        {
            return AppliquerFormat(FormatReference, finPeriode);
        }

        /// <summary>Remplace {AAAA}, {AA} et {MM} (majuscules ou minuscules) par la période.</summary>
        public static string AppliquerFormat(string format, DateTime finPeriode)
        {
            if (string.IsNullOrEmpty(format)) return "";
            return Regex.Replace(format, @"\{(AAAA|AA|MM)\}", m =>
            {
                string jeton = m.Groups[1].Value.ToUpperInvariant();
                if (jeton == "AAAA") return finPeriode.ToString("yyyy", CultureInfo.InvariantCulture);
                if (jeton == "AA") return finPeriode.ToString("yy", CultureInfo.InvariantCulture);
                return finPeriode.ToString("MM", CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase).Trim();
        }

        /// <summary>Compte sans espaces, en majuscules.</summary>
        public static string NettoyerCompte(string compte)
        {
            if (string.IsNullOrEmpty(compte)) return "";
            var sb = new StringBuilder(compte.Length);
            foreach (char c in compte)
            {
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            }
            return sb.ToString().ToUpperInvariant();
        }

        /// <summary>
        /// Complète un compte par des zéros à droite jusqu'à la longueur demandée (pratique
        /// de Sage : 6611 → 661100). Un compte plus long est rendu tel quel : le tronquer
        /// changerait de compte sans prévenir ; l'appelant le signale.
        /// </summary>
        public static string CompleterCompte(string compte, int longueur)
        {
            string c = NettoyerCompte(compte);
            if (longueur <= 0 || c.Length == 0 || c.Length >= longueur) return c;
            return c.PadRight(longueur, '0');
        }

        /// <summary>Longueur du plus long compte, plafonnée à la limite de Sage (13).</summary>
        public static int LongueurAutomatique(IEnumerable<string> comptes)
        {
            int max = 0;
            if (comptes != null)
            {
                foreach (var c in comptes)
                {
                    int n = NettoyerCompte(c).Length;
                    if (n > max) max = n;
                }
            }
            return Math.Min(max, LongueurMaxCompteSage);
        }

        /// <summary>
        /// Contrôles bloquants avant enregistrement. <paramref name="periodeExemple"/> sert
        /// à vérifier la longueur du n° de pièce et de la référence une fois les variables
        /// remplacées.
        /// </summary>
        public List<string> Anomalies(DateTime periodeExemple)
        {
            var liste = new List<string>();

            string journal = (CodeJournal ?? "").Trim();
            if (journal.Length == 0)
                liste.Add("Le code journal est obligatoire.");
            else if (journal.Length > LongueurMaxJournalSage)
                liste.Add(string.Format("Le code journal « {0} » dépasse {1} caractères (limite de Sage 100c).", journal, LongueurMaxJournalSage));
            else if (!EstAlphanumerique(journal))
                liste.Add(string.Format("Le code journal « {0} » ne doit contenir que des lettres et des chiffres.", journal));

            string piece = NumeroPiece(periodeExemple);
            if (piece.Length == 0)
                liste.Add("Le n° de pièce est obligatoire.");
            else if (piece.Length > LongueurMaxPieceSage)
                liste.Add(string.Format("Le n° de pièce obtenu « {0} » dépasse {1} caractères (limite de Sage 100c).", piece, LongueurMaxPieceSage));

            string reference = Reference(periodeExemple);
            if (reference.Length > LongueurMaxReferenceSage)
                liste.Add(string.Format("La référence obtenue « {0} » dépasse {1} caractères (limite de Sage 100c).", reference, LongueurMaxReferenceSage));

            if ((FormatPiece ?? "").Contains(";") || (FormatReference ?? "").Contains(";") || (LibelleJournal ?? "").Contains(";"))
                liste.Add("Le point-virgule est le séparateur du fichier d'import : il ne peut pas figurer dans le journal, la pièce ou la référence.");

            int n;
            if (!int.TryParse(Lire(CleLongueurComptes), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                liste.Add("La longueur des comptes généraux doit être un nombre (0 pour automatique).");
            else if (n != 0 && (n < 3 || n > LongueurMaxCompteSage))
                liste.Add(string.Format("La longueur des comptes généraux doit valoir 0 (automatique) ou être comprise entre 3 et {0}.", LongueurMaxCompteSage));

            foreach (var d in _definitions.Where(x => x.EstCompte))
            {
                string compte = NettoyerCompte(ValeurDe(d.Cle));
                if (compte.Length == 0)
                    liste.Add(string.Format("Compte obligatoire : {0}.", d.Libelle));
                else if (compte.Length > LongueurMaxCompteSage)
                    liste.Add(string.Format("{0} : le compte « {1} » dépasse {2} caractères (limite de Sage 100c).", d.Libelle, compte, LongueurMaxCompteSage));
                else if (!EstAlphanumerique(compte))
                    liste.Add(string.Format("{0} : le compte « {1} » ne doit contenir que des lettres et des chiffres.", d.Libelle, compte));
            }

            return liste;
        }

        private static bool EstAlphanumerique(string texte)
        {
            foreach (char c in texte)
            {
                if (!(c >= '0' && c <= '9') && !(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z'))
                    return false;
            }
            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

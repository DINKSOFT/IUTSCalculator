using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace IUTSCalculator.Models
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  Gestion des temps et des rotations (module RH-08)
    //
    //  Un salarié posté appartient à une ÉQUIPE dont le CYCLE (par exemple 7 jours de
    //  jour, 7 nuits, 7 repos) génère son planning. Le planning se corrige jour par
    //  jour ou s'alimente par un fichier de pointage ; le décompte hebdomadaire en tire
    //  les heures supplémentaires par tranche (PAI-03), les paniers et les absences
    //  non justifiées (PAI-04), sur une période de pointage qui peut être décalée du
    //  mois civil (arrêté au 20, par exemple).
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Semaine sur laquelle s'apprécie la durée légale de 40 heures.</summary>
    public enum ModeSemaineDecompte
    {
        /// <summary>Semaine civile, du lundi au dimanche.</summary>
        Civile = 0,

        /// <summary>Semaine de rotation : elle commence le jour de la semaine où débute le cycle.</summary>
        Rotation = 1
    }

    /// <summary>Valorisation de la prime de panier d'une équipe.</summary>
    public enum ModePanier
    {
        Aucun = 0,

        /// <summary>Montant fixe par poste travaillé.</summary>
        MontantFixe = 1,

        /// <summary>Fraction du taux horaire par poste travaillé (1/3 : 0,3333).</summary>
        FractionTauxHoraire = 2
    }

    /// <summary>Origine d'un jour de planning : ce qui décide s'il peut être régénéré.</summary>
    public enum SourceJourPlanning
    {
        /// <summary>Généré depuis le cycle de l'équipe : régénéré librement.</summary>
        Cycle = 0,

        /// <summary>Saisi ou corrigé à la main : jamais écrasé par une régénération.</summary>
        Saisie = 1,

        /// <summary>Importé depuis un fichier de pointage.</summary>
        Pointage = 2,

        /// <summary>Repris d'une absence enregistrée dans « Congés et absences ».</summary>
        Absence = 3
    }

    /// <summary>Codes du planning, ceux des états de rotation (J, N, R, AJ, A, FJ, FN) plus CP et M.</summary>
    public static class CodesPlanning
    {
        public const string Jour = "J";
        public const string Nuit = "N";
        public const string Repos = "R";
        public const string FerieJour = "FJ";
        public const string FerieNuit = "FN";
        public const string AbsenceJustifiee = "AJ";
        public const string AbsenceNonJustifiee = "A";
        public const string Conge = "CP";
        public const string Maladie = "M";

        private static readonly string[] _tous =
        {
            Jour, Nuit, Repos, FerieJour, FerieNuit, AbsenceJustifiee, AbsenceNonJustifiee, Conge, Maladie
        };

        public static IReadOnlyList<string> Tous
        {
            get { return _tous; }
        }

        /// <summary>« j » → « J », espaces retirés ; null si le code n'existe pas ; "" si vide.</summary>
        public static string Normaliser(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            string c = code.Trim().ToUpperInvariant();
            return _tous.Contains(c) ? c : null;
        }

        public static bool EstTravaille(string code)
        {
            return code == Jour || code == Nuit || code == FerieJour || code == FerieNuit;
        }

        public static bool EstNuit(string code)
        {
            return code == Nuit || code == FerieNuit;
        }

        public static string Libelle(string code)
        {
            switch (code)
            {
                case Jour: return "Travail de jour";
                case Nuit: return "Travail de nuit";
                case Repos: return "Repos";
                case FerieJour: return "Travail un jour férié, de jour";
                case FerieNuit: return "Travail un jour férié, de nuit";
                case AbsenceJustifiee: return "Absence justifiée (rémunérée)";
                case AbsenceNonJustifiee: return "Absence non justifiée (retenue)";
                case Conge: return "Congé payé";
                case Maladie: return "Maladie / accident du travail";
                default: return "Non saisi";
            }
        }

        /// <summary>Légende d'une ligne : « J = jour · N = nuit · … ».</summary>
        public static string Legende()
        {
            return "J jour · N nuit · R repos · FJ/FN férié jour/nuit · AJ absence justifiée · " +
                   "A absence non justifiée · CP congé payé · M maladie";
        }

        /// <summary>Code de planning d'une absence enregistrée dans « Congés et absences ».</summary>
        public static string DepuisAbsence(TypeAbsence type)
        {
            switch (type)
            {
                case TypeAbsence.CongePaye: return Conge;
                case TypeAbsence.Maladie:
                case TypeAbsence.Maternite:
                case TypeAbsence.AccidentTravail: return Maladie;
                case TypeAbsence.AbsenceNonRemuneree:
                case TypeAbsence.MiseAPied: return AbsenceNonJustifiee;
                default: return AbsenceJustifiee;
            }
        }
    }

    /// <summary>
    /// Cycle de rotation, écrit comme sur un tableau de service : « 7J 7N 7R » (7 jours,
    /// 7 nuits, 7 repos), « 4J 4R », ou jour par jour « J J N N R R ». Le cycle se répète
    /// indéfiniment à partir de la date de début de l'équipe.
    /// </summary>
    public sealed class MotifCycle
    {
        private readonly List<string> _jours;

        private MotifCycle(List<string> jours)
        {
            _jours = jours;
        }

        public IReadOnlyList<string> Jours
        {
            get { return _jours; }
        }

        public int Longueur
        {
            get { return _jours.Count; }
        }

        /// <summary>Code du cycle à une date donnée, sachant que le jour 1 du cycle est <paramref name="debutCycle"/>.</summary>
        public string CodePour(DateTime date, DateTime debutCycle)
        {
            if (_jours.Count == 0) return string.Empty;
            int ecart = (date.Date - debutCycle.Date).Days;
            int rang = ((ecart % _jours.Count) + _jours.Count) % _jours.Count;
            return _jours[rang];
        }

        /// <summary>« Cycle de 21 jours : 7 J, 7 N, 7 R ».</summary>
        public string Resume
        {
            get
            {
                if (_jours.Count == 0) return "Cycle vide";
                var groupes = new List<string>();
                int i = 0;
                while (i < _jours.Count)
                {
                    int j = i;
                    while (j < _jours.Count && _jours[j] == _jours[i]) j++;
                    groupes.Add(string.Format("{0} {1}", j - i, _jours[i]));
                    i = j;
                }
                return string.Format("Cycle de {0} jour{1} : {2}", _jours.Count, _jours.Count > 1 ? "s" : "", string.Join(", ", groupes));
            }
        }

        /// <summary>Forme normalisée : « 7J 7N 7R ».</summary>
        public string Normalise
        {
            get
            {
                var sb = new StringBuilder();
                int i = 0;
                while (i < _jours.Count)
                {
                    int j = i;
                    while (j < _jours.Count && _jours[j] == _jours[i]) j++;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(j - i).Append(_jours[i]);
                    i = j;
                }
                return sb.ToString();
            }
        }

        public static bool TryParse(string texte, out MotifCycle motif, out string erreur)
        {
            motif = null;
            erreur = null;

            if (string.IsNullOrWhiteSpace(texte))
            {
                erreur = "Le cycle est vide. Exemple : 7J 7N 7R.";
                return false;
            }

            var jours = new List<string>();
            var jetons = texte.Split(new[] { ' ', ',', ';', '-', '+', '/', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var brut in jetons)
            {
                string jeton = brut.Trim().ToUpperInvariant();
                int k = 0;
                while (k < jeton.Length && char.IsDigit(jeton[k])) k++;

                int nombre = 1;
                if (k > 0 && !int.TryParse(jeton.Substring(0, k), NumberStyles.None, CultureInfo.InvariantCulture, out nombre))
                {
                    erreur = string.Format("« {0} » : nombre illisible.", brut);
                    return false;
                }

                string code = CodesPlanning.Normaliser(jeton.Substring(k));
                if (string.IsNullOrEmpty(code))
                {
                    erreur = string.Format("« {0} » : code inconnu. Codes possibles : {1}.", brut, string.Join(", ", CodesPlanning.Tous));
                    return false;
                }

                if (nombre <= 0 || nombre > 366)
                {
                    erreur = string.Format("« {0} » : le nombre de jours doit être compris entre 1 et 366.", brut);
                    return false;
                }

                for (int n = 0; n < nombre; n++)
                    jours.Add(code);
            }

            if (jours.Count == 0)
            {
                erreur = "Le cycle est vide. Exemple : 7J 7N 7R.";
                return false;
            }

            if (jours.Count > 366)
            {
                erreur = "Un cycle ne peut pas dépasser 366 jours.";
                return false;
            }

            motif = new MotifCycle(jours);
            return true;
        }
    }

    /// <summary>Équipe de travail posté et son cycle de rotation (module RH-08).</summary>
    public class EquipeTravail : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _libelle;
        private string _site;
        private string _motif = "7J 7N 7R";
        private DateTime _dateDebutCycle = DateTime.Today;
        private decimal _dureePosteJour = 12m;
        private decimal _dureePosteNuit = 12m;
        private decimal _heuresNormalesParPoste = 8m;
        private ModeSemaineDecompte _modeSemaine = ModeSemaineDecompte.Civile;
        private bool _dimancheMajore;
        private ModePanier _panierMode = ModePanier.Aucun;
        private decimal _panierValeur;
        private bool _active = true;
        private string _commentaire;
        private int _nombreMembres;

        public int Id
        {
            get { return _id; }
            set { Definir(ref _id, value); }
        }

        /// <summary>Code court affiché dans le planning (« TEAM A »).</summary>
        public string Code
        {
            get { return _code; }
            set { if (Definir(ref _code, value)) OnPropertyChanged(nameof(Designation)); }
        }

        public string Libelle
        {
            get { return _libelle; }
            set { if (Definir(ref _libelle, value)) OnPropertyChanged(nameof(Designation)); }
        }

        /// <summary>Site d'affectation (« YOUGA »).</summary>
        public string Site
        {
            get { return _site; }
            set { Definir(ref _site, value); }
        }

        /// <summary>Cycle de rotation, « 7J 7N 7R ».</summary>
        public string Motif
        {
            get { return _motif; }
            set
            {
                if (Definir(ref _motif, value))
                {
                    OnPropertyChanged(nameof(ResumeCycle));
                    OnPropertyChanged(nameof(CycleValide));
                }
            }
        }

        /// <summary>Premier jour du cycle (jour 1 du motif).</summary>
        public DateTime DateDebutCycle
        {
            get { return _dateDebutCycle; }
            set { Definir(ref _dateDebutCycle, value.Date); }
        }

        /// <summary>Heures d'un poste de jour (12).</summary>
        public decimal DureePosteJour
        {
            get { return _dureePosteJour; }
            set { Definir(ref _dureePosteJour, value); }
        }

        /// <summary>Heures d'un poste de nuit (12).</summary>
        public decimal DureePosteNuit
        {
            get { return _dureePosteNuit; }
            set { Definir(ref _dureePosteNuit, value); }
        }

        /// <summary>
        /// Heures d'un poste considérées comme normales (8) : au-delà, les heures d'une nuit
        /// ou d'un jour férié sont des heures supplémentaires à majoration propre.
        /// </summary>
        public decimal HeuresNormalesParPoste
        {
            get { return _heuresNormalesParPoste; }
            set { Definir(ref _heuresNormalesParPoste, value); }
        }

        public ModeSemaineDecompte ModeSemaine
        {
            get { return _modeSemaine; }
            set { Definir(ref _modeSemaine, value); }
        }

        /// <summary>Le travail du dimanche est-il majoré comme un jour férié ?</summary>
        public bool DimancheMajore
        {
            get { return _dimancheMajore; }
            set { Definir(ref _dimancheMajore, value); }
        }

        public ModePanier PanierMode
        {
            get { return _panierMode; }
            set { Definir(ref _panierMode, value); }
        }

        /// <summary>Montant d'un panier (mode fixe) ou fraction du taux horaire (0,3333).</summary>
        public decimal PanierValeur
        {
            get { return _panierValeur; }
            set { Definir(ref _panierValeur, value); }
        }

        public bool Active
        {
            get { return _active; }
            set { Definir(ref _active, value); }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { Definir(ref _commentaire, value); }
        }

        /// <summary>Nombre de salariés affectés (affichage).</summary>
        public int NombreMembres
        {
            get { return _nombreMembres; }
            set { Definir(ref _nombreMembres, value); }
        }

        public string Designation
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Libelle)) return Code ?? string.Empty;
                return string.Format("{0} — {1}", Code, Libelle);
            }
        }

        /// <summary>Premier jour de la semaine de décompte.</summary>
        public DayOfWeek PremierJourSemaine
        {
            get { return ModeSemaine == ModeSemaineDecompte.Rotation ? DateDebutCycle.DayOfWeek : DayOfWeek.Monday; }
        }

        public bool CycleValide
        {
            get
            {
                MotifCycle m;
                string e;
                return MotifCycle.TryParse(Motif, out m, out e);
            }
        }

        public string ResumeCycle
        {
            get
            {
                MotifCycle m;
                string e;
                return MotifCycle.TryParse(Motif, out m, out e) ? m.Resume : e;
            }
        }

        public MotifCycle Cycle()
        {
            MotifCycle m;
            string e;
            return MotifCycle.TryParse(Motif, out m, out e) ? m : null;
        }

        /// <summary>Durée d'un poste selon son code (jour/nuit).</summary>
        public decimal DureePoste(string code)
        {
            return CodesPlanning.EstNuit(code) ? DureePosteNuit : DureePosteJour;
        }

        public List<string> Anomalies()
        {
            var anomalies = new List<string>();

            if (string.IsNullOrWhiteSpace(Code))
                anomalies.Add("Le code de l'équipe est obligatoire.");

            MotifCycle m;
            string erreur;
            if (!MotifCycle.TryParse(Motif, out m, out erreur))
                anomalies.Add("Cycle : " + erreur);

            if (DureePosteJour <= 0m || DureePosteJour > 24m)
                anomalies.Add("La durée d'un poste de jour doit être comprise entre 1 et 24 heures.");
            if (DureePosteNuit <= 0m || DureePosteNuit > 24m)
                anomalies.Add("La durée d'un poste de nuit doit être comprise entre 1 et 24 heures.");
            if (HeuresNormalesParPoste <= 0m || HeuresNormalesParPoste > 24m)
                anomalies.Add("Les heures normales d'un poste doivent être comprises entre 1 et 24.");
            if (PanierMode != ModePanier.Aucun && PanierValeur <= 0m)
                anomalies.Add("Indiquez la valeur du panier (montant, ou fraction du taux horaire : 0,3333 pour un tiers).");
            if (PanierMode == ModePanier.FractionTauxHoraire && PanierValeur > 24m)
                anomalies.Add("La fraction du taux horaire d'un panier paraît excessive (au-delà de 24 heures).");

            return anomalies;
        }

        public EquipeTravail Clone()
        {
            return new EquipeTravail
            {
                Id = Id,
                Code = Code,
                Libelle = Libelle,
                Site = Site,
                Motif = Motif,
                DateDebutCycle = DateDebutCycle,
                DureePosteJour = DureePosteJour,
                DureePosteNuit = DureePosteNuit,
                HeuresNormalesParPoste = HeuresNormalesParPoste,
                ModeSemaine = ModeSemaine,
                DimancheMajore = DimancheMajore,
                PanierMode = PanierMode,
                PanierValeur = PanierValeur,
                Active = Active,
                Commentaire = Commentaire,
                NombreMembres = NombreMembres
            };
        }

        public override string ToString()
        {
            return Designation;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Definir<T>(ref T champ, T valeur, [CallerMemberName] string nom = null)
        {
            if (Equals(champ, valeur)) return false;
            champ = valeur;
            OnPropertyChanged(nom);
            return true;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string nom = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
        }
    }

    /// <summary>Un jour du planning d'un salarié.</summary>
    public class PlanningJour
    {
        public long Id { get; set; }
        public string EmployeeId { get; set; }
        public DateTime Date { get; set; }

        /// <summary>Code normalisé (J, N, R…).</summary>
        public string Code { get; set; }

        /// <summary>Heures réellement faites, quand elles diffèrent de la durée du poste (pointage).</summary>
        public decimal? Heures { get; set; }

        /// <summary>Équipe du salarié ce jour-là (null : équipe actuelle).</summary>
        public int? EquipeId { get; set; }

        public SourceJourPlanning Source { get; set; }
        public string Commentaire { get; set; }
    }

    /// <summary>Jour férié chômé et payé.</summary>
    public class JourFerie
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Libelle { get; set; }

        public string Designation
        {
            get { return string.Format(CultureInfo.GetCultureInfo("fr-FR"), "{0:dddd dd/MM/yyyy} — {1}", Date, Libelle); }
        }
    }

    /// <summary>Fête légale dont la date change chaque année (Ascension, fêtes musulmanes).</summary>
    public sealed class FeteMobile
    {
        private readonly string[] _motsCles;

        public FeteMobile(string libelle, params string[] motsCles)
        {
            Libelle = libelle;
            _motsCles = motsCles ?? new string[0];
        }

        public string Libelle { get; }

        /// <summary>
        /// Le nom d'un jour férié désigne-t-il cette fête ? (« Tabaski », « Aïd el-Kébir »,
        /// « fête de la Tabaski »…) : sert à ne pas créer de doublon d'une fête déjà saisie à
        /// la main, même à une autre date.
        /// </summary>
        public bool Designe(string libelle)
        {
            string n = JoursFeriesLegaux.Normaliser(libelle);
            return n.Length > 0 && _motsCles.Any(m => n.Contains(m));
        }
    }

    /// <summary>
    /// Jours chômés et payés du Burkina Faso, pré-chargés dans la liste des jours fériés.
    ///
    /// Texte en vigueur : loi n° 001-2026/ALT du 9 janvier 2026 portant institution de jours
    /// chômés et payés et de journées de commémoration et de recueillement (elle abroge la
    /// loi de novembre 2015). Onze jours chômés et payés : 1er janvier, 8 mars, 1er mai,
    /// 15 mai (journée des coutumes et traditions), 15 août, 11 décembre, 25 décembre,
    /// Ascension, Maouloud, fin du Ramadan, Tabaski. Le lundi de Pâques n'en fait plus
    /// partie ; les 3 janvier, 4 et 5 août, 15 et 31 octobre et 1er novembre deviennent des
    /// journées de commémoration travaillées ; une fête tombant un dimanche ne reporte plus
    /// le repos au lundi. Sources : Burkina24 et Wakat Séra (9 janvier 2026), Sidwaya.
    ///
    /// Ascension : 39 jours après Pâques (calcul grégorien). Fêtes musulmanes : leur date est
    /// fixée chaque année d'après la Fédération des associations islamiques du Burkina
    /// (FAIB) et annoncée par le ministère chargé du travail. Seules les dates publiées
    /// officiellement figurent ci-dessous — aucune n'est estimée ; les autres se saisissent
    /// à la main dès leur annonce.
    /// </summary>
    public static class JoursFeriesLegaux
    {
        /// <summary>Première année couverte : celle de la loi n° 001-2026/ALT.</summary>
        public const int PremiereAnnee = 2026;

        public const string Reference = "loi n° 001-2026/ALT du 9 janvier 2026";

        public static readonly FeteMobile Ascension = new FeteMobile("Ascension", "ascension");
        public static readonly FeteMobile FinRamadan = new FeteMobile("Aïd el-Fitr (fin du Ramadan)", "ramadan", "fitr", "korite");
        public static readonly FeteMobile Tabaski = new FeteMobile("Tabaski (Aïd el-Kébir)", "tabaski", "kebir", "adha");
        public static readonly FeteMobile Maouloud = new FeteMobile("Maouloud", "maouloud", "mouloud", "mawlid", "maoulid");

        /// <summary>Fêtes musulmanes, dans l'ordre de l'année.</summary>
        public static readonly IReadOnlyList<FeteMobile> FetesMusulmanes = new[] { FinRamadan, Tabaski, Maouloud };

        // Dates officielles des fêtes musulmanes, année par année. N'ajouter une date qu'avec
        // sa source (communiqué du ministère ou de la FAIB) : une date estimée fausserait le
        // décompte des heures majorées.
        private static readonly Dictionary<int, Dictionary<FeteMobile, DateTime>> _datesPubliees =
            new Dictionary<int, Dictionary<FeteMobile, DateTime>>
            {
                {
                    2026, new Dictionary<FeteMobile, DateTime>
                    {
                        // Vendredi 20 mars 2026 déclaré chômé et payé (communiqué du ministère
                        // des Serviteurs du Peuple, Burkina24 du 19/03/2026).
                        { FinRamadan, new DateTime(2026, 3, 20) },
                        // Mercredi 27 mai 2026 (communiqué de la FAIB du 17/05/2026, Burkina24 et APA du 18/05/2026).
                        { Tabaski, new DateTime(2026, 5, 27) },
                        // Mercredi 26 août 2026 déclaré chômé et payé, article 2 de la loi
                        // n° 001-2026/ALT (communiqué du ministère, Burkina24 du 24/08/2026).
                        { Maouloud, new DateTime(2026, 8, 26) }
                    }
                }
            };

        /// <summary>Dimanche de Pâques (calendrier grégorien, algorithme de Meeus/Jones/Butcher).</summary>
        public static DateTime Paques(int annee)
        {
            int a = annee % 19;
            int b = annee / 100;
            int c = annee % 100;
            int d = b / 4;
            int e = b % 4;
            int f = (b + 8) / 25;
            int g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30;
            int i = c / 4;
            int k = c % 4;
            int l = (32 + 2 * e + 2 * i - h - k) % 7;
            int m = (a + 11 * h + 22 * l) / 451;
            int mois = (h + l - 7 * m + 114) / 31;
            int jour = ((h + l - 7 * m + 114) % 31) + 1;
            return new DateTime(annee, mois, jour);
        }

        /// <summary>Jeudi de l'Ascension : Pâques + 39 jours.</summary>
        public static DateTime DateAscension(int annee)
        {
            return Paques(annee).AddDays(39);
        }

        /// <summary>Date officielle d'une fête musulmane pour une année, ou null si elle n'est pas (encore) publiée.</summary>
        public static DateTime? DatePubliee(FeteMobile fete, int annee)
        {
            Dictionary<FeteMobile, DateTime> dates;
            DateTime date;
            if (fete != null && _datesPubliees.TryGetValue(annee, out dates) && dates.TryGetValue(fete, out date))
                return date;
            return null;
        }

        /// <summary>
        /// Jours chômés et payés d'une année : les sept jours à date fixe, l'Ascension et les
        /// fêtes musulmanes dont la date est publiée. Deux fêtes le même jour n'en font qu'un.
        /// Liste vide avant <see cref="PremiereAnnee"/>.
        /// </summary>
        public static List<JourFerie> PourAnnee(int annee)
        {
            return Regrouper(Elements(annee));
        }

        /// <summary>
        /// Jours légaux d'une année qui manquent à la liste du dossier. Une date déjà présente
        /// (saisie à la main ou chargée auparavant) n'est jamais remplacée ; une fête à date
        /// variable déjà saisie sous son nom, même à une autre date, n'est pas ajoutée une
        /// seconde fois. Rien n'est jamais retiré : relancer le chargement n'a pas d'effet.
        /// </summary>
        /// <param name="dejaPresents">Jours légaux de l'année déjà présents dans la liste.</param>
        public static List<JourFerie> AAjouter(int annee, IEnumerable<JourFerie> existants, out int dejaPresents)
        {
            var liste = (existants ?? Enumerable.Empty<JourFerie>()).Where(f => f != null).ToList();
            var dates = new HashSet<DateTime>(liste.Select(f => f.Date.Date));
            var deLAnnee = liste.Where(f => f.Date.Year == annee).ToList();

            var manquants = new List<KeyValuePair<DateTime, string>>();
            dejaPresents = 0;
            foreach (var e in Elements(annee))
            {
                bool present = dates.Contains(e.Item1)
                               || (e.Item3 != null && deLAnnee.Any(x => e.Item3.Designe(x.Libelle)));
                if (present) dejaPresents++;
                else manquants.Add(new KeyValuePair<DateTime, string>(e.Item1, e.Item2));
            }
            return Regrouper(manquants.Select(p => Tuple.Create(p.Key, p.Value, (FeteMobile)null)));
        }

        /// <summary>Jours légaux un par un : date, libellé, fête à date variable (null pour une date fixe).</summary>
        private static List<Tuple<DateTime, string, FeteMobile>> Elements(int annee)
        {
            var liste = new List<Tuple<DateTime, string, FeteMobile>>();
            if (annee < PremiereAnnee || annee > 9999) return liste;

            Action<DateTime, string, FeteMobile> ajouter = (date, libelle, fete) => liste.Add(Tuple.Create(date.Date, libelle, fete));
            ajouter(new DateTime(annee, 1, 1), "Jour de l'An", null);
            ajouter(new DateTime(annee, 3, 8), "Journée internationale des droits de la femme", null);
            ajouter(new DateTime(annee, 5, 1), "Fête du Travail", null);
            ajouter(new DateTime(annee, 5, 15), "Journée des coutumes et traditions", null);
            ajouter(new DateTime(annee, 8, 15), "Assomption", null);
            ajouter(new DateTime(annee, 12, 11), "Fête nationale", null);
            ajouter(new DateTime(annee, 12, 25), "Noël", null);
            ajouter(DateAscension(annee), Ascension.Libelle, Ascension);
            foreach (var fete in FetesMusulmanes)
            {
                var date = DatePubliee(fete, annee);
                if (date.HasValue) ajouter(date.Value, fete.Libelle, fete);
            }
            return liste;
        }

        /// <summary>Un jour férié par date, dans l'ordre : deux fêtes le même jour partagent la date (« Fête du Travail / Ascension »).</summary>
        private static List<JourFerie> Regrouper(IEnumerable<Tuple<DateTime, string, FeteMobile>> elements)
        {
            var parDate = new SortedDictionary<DateTime, string>();
            foreach (var e in elements)
            {
                string existant;
                parDate[e.Item1] = parDate.TryGetValue(e.Item1, out existant) ? existant + " / " + e.Item2 : e.Item2;
            }
            return parDate.Select(p => new JourFerie { Date = p.Key, Libelle = p.Value }).ToList();
        }

        /// <summary>
        /// Fêtes musulmanes de l'année absentes de la liste du dossier : ni pré-chargées (date
        /// non publiée), ni saisies à la main. Ce sont celles à rappeler à l'écran.
        /// </summary>
        public static List<FeteMobile> FetesAMettreAJour(int annee, IEnumerable<JourFerie> existants)
        {
            var deLAnnee = (existants ?? Enumerable.Empty<JourFerie>()).Where(f => f != null && f.Date.Year == annee).ToList();
            return FetesMusulmanes.Where(f => !deLAnnee.Any(x => f.Designe(x.Libelle))).ToList();
        }

        /// <summary>Rappel affiché à l'écran pour les fêtes dont la date reste à saisir.</summary>
        public static string Rappel(int annee, IEnumerable<string> fetes)
        {
            var liste = (fetes ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            if (liste.Count == 0) return string.Empty;
            return string.Format(
                "À saisir à la main pour {0} : {1}. Leur date n'est pas encore publiée (elle est fixée chaque année " +
                "d'après la FAIB et annoncée par communiqué du ministère) : ajoutez chaque fête dès son annonce officielle.",
                annee, string.Join(", ", liste));
        }

        /// <summary>Minuscules, sans accents ni ponctuation : « Aïd el-Kébir » → « aid el kebir ».</summary>
        public static string Normaliser(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return string.Empty;
            var decompose = texte.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decompose.Length);
            foreach (char ch in decompose)
            {
                var categorie = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (categorie == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }
            return sb.ToString();
        }
    }

    /// <summary>Paramètres du relevé des temps propres au dossier.</summary>
    public class ParametresTemps
    {
        /// <summary>
        /// Jour d'arrêté de la période de pointage. 0 : mois civil. 20 : la paie d'octobre
        /// porte sur la période du 21 septembre au 20 octobre.
        /// </summary>
        public int JourArrete { get; set; }

        /// <summary>
        /// Une absence justifiée compte-t-elle (8 heures) pour atteindre le seuil hebdomadaire
        /// des 40 heures ? Non par défaut : les heures supplémentaires se décomptent sur le
        /// travail effectif.
        /// </summary>
        public bool AJCompteDansSeuil { get; set; }
    }

    /// <summary>Période de pointage d'un mois de paie.</summary>
    public sealed class PeriodePointage
    {
        public int Annee { get; private set; }
        public int Mois { get; private set; }
        public DateTime Debut { get; private set; }
        public DateTime Fin { get; private set; }

        public int NombreJours
        {
            get { return (Fin - Debut).Days + 1; }
        }

        /// <summary>
        /// Période du mois de paie (<paramref name="annee"/>, <paramref name="mois"/>).
        /// Jour d'arrêté entre 1 et 27 : du lendemain de l'arrêté du mois précédent au jour
        /// d'arrêté du mois. 0 ou au-delà de 27 : le mois civil.
        /// </summary>
        public static PeriodePointage Pour(int annee, int mois, int jourArrete)
        {
            var premier = new DateTime(annee, mois, 1);
            if (jourArrete <= 0 || jourArrete >= 28)
            {
                return new PeriodePointage
                {
                    Annee = annee, Mois = mois,
                    Debut = premier,
                    Fin = premier.AddMonths(1).AddDays(-1)
                };
            }

            var precedent = premier.AddMonths(-1);
            return new PeriodePointage
            {
                Annee = annee, Mois = mois,
                Debut = new DateTime(precedent.Year, precedent.Month, jourArrete).AddDays(1),
                Fin = new DateTime(annee, mois, jourArrete)
            };
        }

        public bool Contient(DateTime date)
        {
            return date.Date >= Debut && date.Date <= Fin;
        }

        public string Libelle
        {
            get { return string.Format("du {0:dd/MM/yyyy} au {1:dd/MM/yyyy}", Debut, Fin); }
        }
    }

    /// <summary>Décompte d'une semaine (affichage du détail d'un salarié).</summary>
    public class SemaineDecompte
    {
        public DateTime Debut { get; set; }
        public DateTime Fin { get; set; }
        public decimal HeuresTravaillees { get; set; }
        public decimal HeuresNormales { get; set; }
        public decimal HeuresSup { get; set; }
        public decimal Tranche1 { get; set; }
        public decimal Tranche2 { get; set; }
        public decimal Nuit { get; set; }
        public decimal FerieJour { get; set; }
        public decimal FerieNuit { get; set; }

        /// <summary>« J J J N N R R ».</summary>
        public string Codes { get; set; }

        public string Libelle
        {
            get { return string.Format("{0:dd/MM} → {1:dd/MM}", Debut, Fin); }
        }
    }

    /// <summary>Récapitulatif d'un salarié sur une période de pointage.</summary>
    public class RecapTempsSalarie
    {
        public RecapTempsSalarie()
        {
            Semaines = new List<SemaineDecompte>();
            AbsencesNonJustifiees = new List<PlanningJour>();
            Avertissements = new List<string>();
        }

        public string EmployeeId { get; set; }
        public string NomComplet { get; set; }
        public string EquipeCode { get; set; }
        public decimal SalaireDeBase { get; set; }

        // Jours de la période
        public int JoursTravailles { get; set; }
        public int JoursRepos { get; set; }
        public int JoursAbsenceJustifiee { get; set; }
        /// <summary>Jours « A » qui font manquer un poste prévu par le cycle (les absences à retenir).</summary>
        public int JoursAbsenceNonJustifiee { get; set; }

        /// <summary>Jours « A » posés sur un repos prévu par le cycle : 0 h, aucune absence retenue.</summary>
        public int JoursAbsenceSurRepos { get; set; }

        public int JoursConge { get; set; }
        public int JoursMaladie { get; set; }
        public int JoursNonSaisis { get; set; }
        public decimal HeuresPeriode { get; set; }

        // Semaines rattachées à la période
        public DateTime? DebutDecompte { get; set; }
        public DateTime? FinDecompte { get; set; }
        public decimal HeuresDecomptees { get; set; }
        public decimal HeuresNormales { get; set; }
        public decimal HeuresSup { get; set; }
        public decimal Tranche1 { get; set; }
        public decimal Tranche2 { get; set; }
        public decimal Nuit { get; set; }
        public decimal FerieJour { get; set; }
        public decimal FerieNuit { get; set; }

        // Paniers
        public int Paniers { get; set; }
        public decimal MontantPanier { get; set; }

        /// <summary>L'équipe du salarié verse-t-elle une prime de panier ?</summary>
        public bool PanierActif { get; set; }

        public List<SemaineDecompte> Semaines { get; private set; }

        /// <summary>
        /// Jours « A » de la période qui font manquer un poste, avec les heures du poste que le
        /// cycle prévoyait ce jour-là (null : salarié sans équipe, heures inconnues).
        /// </summary>
        public List<PlanningJour> AbsencesNonJustifiees { get; private set; }

        public List<string> Avertissements { get; private set; }

        public string Observations
        {
            get { return string.Join(" ", Avertissements); }
        }

        public string PeriodeDecompteLibelle
        {
            get
            {
                if (!DebutDecompte.HasValue || !FinDecompte.HasValue) return "—";
                return string.Format("{0:dd/MM} → {1:dd/MM}", DebutDecompte.Value, FinDecompte.Value);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Module RH-07 — Santé & sécurité au travail
    //
    //  Trois registres : les visites médicales et leurs échéances, les équipements
    //  de protection individuelle remis à chaque salarié, les accidents du travail.
    //
    //  Règles CNSS (loi n° 004-2021/AN, site de la CNSS) reprises ici : déclaration de
    //  l'accident à la CNSS ET à l'inspection du travail dans les 48 heures ouvrables ;
    //  jour de l'accident entièrement à la charge de l'employeur ; à partir du
    //  lendemain, indemnité journalière de la CNSS égale aux 2/3 de la rémunération
    //  journalière moyenne, chaque jour d'incapacité, ouvrable ou non ; le tiers restant
    //  à la charge de l'employeur. Choix du client (19/10/2026) : salaire maintenu en
    //  entier, les indemnités journalières étant réclamées à la CNSS.
    // ═══════════════════════════════════════════════════════════════════════

    public enum TypeVisiteMedicale
    {
        Embauche = 0,
        Periodique = 1,
        Reprise = 2,
        ALaDemande = 3
    }

    public enum AptitudeMedicale
    {
        NonRenseignee = 0,
        Apte = 1,
        AptAvecReserves = 2,
        InapteTemporaire = 3,
        InapteDefinitif = 4
    }

    public enum NatureAccident
    {
        Travail = 0,
        Trajet = 1,
        MaladieProfessionnelle = 2
    }

    public enum EtatDotation
    {
        EnService = 0,
        Rendu = 1,
        PerduOuUse = 2
    }

    /// <summary>Avancement du remboursement des indemnités journalières par la CNSS.</summary>
    public enum StatutRemboursementIJ
    {
        ADeposer = 0,
        Depose = 1,
        RemboursePartiellement = 2,
        Rembourse = 3
    }

    public static class LibellesSST
    {
        public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static string Visite(TypeVisiteMedicale t)
        {
            switch (t)
            {
                case TypeVisiteMedicale.Embauche: return "Visite d'embauche";
                case TypeVisiteMedicale.Periodique: return "Visite périodique";
                case TypeVisiteMedicale.Reprise: return "Visite de reprise";
                default: return "Visite à la demande";
            }
        }

        public static string Aptitude(AptitudeMedicale a)
        {
            switch (a)
            {
                case AptitudeMedicale.Apte: return "Apte";
                case AptitudeMedicale.AptAvecReserves: return "Apte avec réserves";
                case AptitudeMedicale.InapteTemporaire: return "Inapte temporaire";
                case AptitudeMedicale.InapteDefinitif: return "Inapte définitif";
                default: return "—";
            }
        }

        public static string Nature(NatureAccident n)
        {
            switch (n)
            {
                case NatureAccident.Trajet: return "Accident de trajet";
                case NatureAccident.MaladieProfessionnelle: return "Maladie professionnelle";
                default: return "Accident du travail";
            }
        }

        public static string Etat(EtatDotation e)
        {
            switch (e)
            {
                case EtatDotation.Rendu: return "Rendu";
                case EtatDotation.PerduOuUse: return "Perdu / hors d'usage";
                default: return "En service";
            }
        }

        public static string Remboursement(StatutRemboursementIJ s)
        {
            switch (s)
            {
                case StatutRemboursementIJ.Depose: return "Déposé";
                case StatutRemboursementIJ.RemboursePartiellement: return "Remboursé partiellement";
                case StatutRemboursementIJ.Rembourse: return "Remboursé";
                default: return "À déposer";
            }
        }

        public static string Date(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("dd/MM/yyyy", Fr) : "—";
        }

        /// <summary>Sièges de lésion proposés à la saisie (texte libre accepté).</summary>
        public static readonly IReadOnlyList<string> SiegesLesion = new[]
        {
            "Tête", "Yeux", "Cou", "Dos / colonne", "Thorax", "Abdomen", "Membres supérieurs", "Mains / doigts",
            "Membres inférieurs", "Pieds / orteils", "Sièges multiples", "Lésions internes"
        };

        /// <summary>Natures de lésion proposées à la saisie (texte libre accepté).</summary>
        public static readonly IReadOnlyList<string> NaturesLesion = new[]
        {
            "Plaie / coupure", "Contusion", "Entorse / foulure", "Fracture", "Brûlure", "Lumbago / effort",
            "Corps étranger", "Intoxication", "Électrisation", "Amputation", "Choc / commotion", "Autre"
        };

        /// <summary>EPI proposés à la création du catalogue.</summary>
        public static readonly IReadOnlyList<string> EpiCourants = new[]
        {
            "Casque de sécurité", "Chaussures de sécurité", "Gants de protection", "Lunettes de protection",
            "Bouchons / casque anti-bruit", "Masque respiratoire", "Gilet haute visibilité", "Combinaison de travail",
            "Harnais antichute", "Bottes de sécurité"
        };
    }

    /// <summary>
    /// Paramètres SST du dossier. Les délais de visites ne sont pas fixés ici par la loi
    /// (ils dépendent du service de santé au travail et de la convention) : valeurs
    /// indicatives, modifiables. Les règles CNSS (48 h ouvrables, 2/3) sont celles en vigueur.
    /// </summary>
    public class ParametresSST
    {
        /// <summary>Périodicité des visites périodiques (mois), sauf poste à périodicité propre.</summary>
        public int PeriodiciteVisiteMois { get; set; } = 12;

        /// <summary>Une visite de reprise est attendue après un arrêt AT/MP ou un arrêt maladie au-delà de ce nombre de jours.</summary>
        public int RepriseApresArretJours { get; set; } = 21;

        /// <summary>Délai de déclaration à la CNSS et à l'inspection du travail, en jours ouvrables (48 h ouvrables = 2).</summary>
        public int DelaiDeclarationJoursOuvrables { get; set; } = 2;

        /// <summary>Part de la rémunération journalière versée par la CNSS (2/3).</summary>
        public decimal TauxIndemniteJournaliere { get; set; } = 2m / 3m;

        /// <summary>Jours du mois de référence pour la rémunération journalière (salaire mensuel ÷ 30).</summary>
        public int JoursMoisReference { get; set; } = 30;

        /// <summary>Alerte « à prévoir » : nombre de jours avant une échéance (visite, renouvellement d'EPI).</summary>
        public int AlerteAvanceJours { get; set; } = 30;

        public static ParametresSST Defaut() { return new ParametresSST(); }
        public ParametresSST Clone() { return (ParametresSST)MemberwiseClone(); }

        /// <summary>Taux affiché en pourcentage (66,67 %).</summary>
        public decimal TauxIndemniteJournalierePourcent
        {
            get { return Math.Round(TauxIndemniteJournaliere * 100m, 2); }
            set { TauxIndemniteJournaliere = Math.Abs(value - 66.67m) < 0.005m ? 2m / 3m : value / 100m; }
        }
    }

    /// <summary>Périodicité propre à un poste (RH-02) : 6 mois pour un poste à risque, par exemple.</summary>
    public class PeriodicitePoste : INotifyPropertyChanged
    {
        private int? _mois;

        public int PosteId { get; set; }
        public string Code { get; set; }
        public string Intitule { get; set; }
        public int Titulaires { get; set; }

        /// <summary>Périodicité propre (mois) ; vide : celle du dossier.</summary>
        public int? Mois
        {
            get { return _mois; }
            set
            {
                int? v = value.HasValue && value.Value <= 0 ? (int?)null : value;
                if (_mois == v) return;
                _mois = v;
                Changer();
            }
        }

        public string Designation { get { return string.IsNullOrWhiteSpace(Code) ? Intitule : Code + " — " + Intitule; } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Changer([CallerMemberName] string nom = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(nom));
        }
    }

    /// <summary>Une visite médicale (prévue ou passée) d'un salarié.</summary>
    public class VisiteMedicale
    {
        public int Id { get; set; }
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public TypeVisiteMedicale Type { get; set; } = TypeVisiteMedicale.Periodique;

        /// <summary>Date de convocation ; vide si la visite n'a pas été programmée.</summary>
        public DateTime? DatePrevue { get; set; }

        /// <summary>Date à laquelle la visite a eu lieu ; vide tant qu'elle n'a pas eu lieu.</summary>
        public DateTime? DateVisite { get; set; }

        public string Medecin { get; set; }
        public AptitudeMedicale Aptitude { get; set; }
        public string Restrictions { get; set; }

        /// <summary>Prochaine visite fixée par le médecin (prime sur la périodicité).</summary>
        public DateTime? ProchaineVisite { get; set; }

        public int? AccidentId { get; set; }
        public string Commentaire { get; set; }

        public bool EstRealisee { get { return DateVisite.HasValue; } }
        public string TypeLibelle { get { return LibellesSST.Visite(Type); } }
        public string AptitudeLibelle { get { return EstRealisee ? LibellesSST.Aptitude(Aptitude) : "Programmée"; } }
        public string DateLibelle { get { return LibellesSST.Date(DateVisite ?? DatePrevue); } }
        public bool EstNouvelle { get { return Id <= 0; } }

        public VisiteMedicale Clone() { return (VisiteMedicale)MemberwiseClone(); }
    }

    /// <summary>Un EPI du catalogue, avec sa durée de vie (renouvellement).</summary>
    public class TypeEPI
    {
        public int Id { get; set; }
        public string Libelle { get; set; }

        /// <summary>Durée de vie en mois ; 0 : pas de renouvellement systématique.</summary>
        public int DureeVieMois { get; set; } = 12;

        public string Norme { get; set; }
        public bool Actif { get; set; } = true;

        public string DureeLibelle { get { return DureeVieMois > 0 ? DureeVieMois + " mois" : "Sans échéance"; } }
        public TypeEPI Clone() { return (TypeEPI)MemberwiseClone(); }
    }

    /// <summary>Une remise d'EPI à un salarié.</summary>
    public class DotationEPI
    {
        public int Id { get; set; }
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public int TypeEpiId { get; set; }
        public string TypeEpiLibelle { get; set; }
        public DateTime DateRemise { get; set; } = DateTime.Today;
        public int Quantite { get; set; } = 1;
        public string Taille { get; set; }

        /// <summary>Renouvellement prévu : remise + durée de vie de l'EPI (modifiable).</summary>
        public DateTime? DateRenouvellement { get; set; }

        public EtatDotation Etat { get; set; }
        public DateTime? DateRetour { get; set; }
        public string Commentaire { get; set; }

        public string EtatLibelle { get { return LibellesSST.Etat(Etat); } }
        public string RenouvellementLibelle { get { return Etat == EtatDotation.EnService ? LibellesSST.Date(DateRenouvellement) : "—"; } }
        public bool EstNouvelle { get { return Id <= 0; } }
        public DotationEPI Clone() { return (DotationEPI)MemberwiseClone(); }
    }

    /// <summary>
    /// Un accident du travail, de trajet, ou une maladie professionnelle. La déclaration
    /// officielle se fait sur l'imprimé de la CNSS : la fiche produite ici en rassemble
    /// les éléments, et le module suit les délais, l'arrêt et les indemnités.
    /// </summary>
    public class AccidentTravail : INotifyPropertyChanged
    {
        private int _id;
        private string _reference;
        private string _employeeId;
        private string _nomSalarie;
        private NatureAccident _nature;
        private DateTime _dateAccident = DateTime.Today;
        private string _heure;
        private string _lieu;
        private string _circonstances;
        private string _siegeLesion;
        private string _natureLesion;
        private string _temoins;
        private string _premiersSoins;
        private bool _avecArret;
        private DateTime? _debutArret;
        private DateTime? _finArret;
        private DateTime? _dateReprise;
        private bool _mortel;
        private DateTime? _dateDeclarationCnss;
        private DateTime? _dateDeclarationInspection;
        private string _referenceCnss;
        private decimal _salaireJournalier;
        private decimal _tauxIpp;
        private int? _absenceId;
        private string _commentaire;
        private DateTime? _dateDepotIJ;
        private decimal _montantRembourseIJ;
        private DateTime? _dateRemboursementIJ;

        public int Id { get { return _id; } set { Set(ref _id, value); } }
        public string Reference { get { return _reference; } set { Set(ref _reference, value); } }
        public string EmployeeId { get { return _employeeId; } set { Set(ref _employeeId, value); } }
        public string NomSalarie { get { return _nomSalarie; } set { Set(ref _nomSalarie, value); } }
        public NatureAccident Nature { get { return _nature; } set { Set(ref _nature, value); } }
        public DateTime DateAccident { get { return _dateAccident; } set { Set(ref _dateAccident, value.Date); } }

        /// <summary>Heure de l'accident (« 14 h 30 »).</summary>
        public string Heure { get { return _heure; } set { Set(ref _heure, value); } }

        public string Lieu { get { return _lieu; } set { Set(ref _lieu, value); } }
        public string Circonstances { get { return _circonstances; } set { Set(ref _circonstances, value); } }
        public string SiegeLesion { get { return _siegeLesion; } set { Set(ref _siegeLesion, value); } }
        public string NatureLesion { get { return _natureLesion; } set { Set(ref _natureLesion, value); } }
        public string Temoins { get { return _temoins; } set { Set(ref _temoins, value); } }

        /// <summary>Premiers soins et centre de prise en charge (à la charge de l'employeur).</summary>
        public string PremiersSoins { get { return _premiersSoins; } set { Set(ref _premiersSoins, value); } }

        public bool AvecArret { get { return _avecArret; } set { Set(ref _avecArret, value); } }

        /// <summary>Premier jour d'arrêt (en principe le lendemain de l'accident).</summary>
        public DateTime? DebutArret { get { return _debutArret; } set { Set(ref _debutArret, D(value)); } }

        /// <summary>Dernier jour d'arrêt (prévu, puis réel) ; vide tant qu'il n'est pas connu.</summary>
        public DateTime? FinArret { get { return _finArret; } set { Set(ref _finArret, D(value)); } }

        public DateTime? DateReprise { get { return _dateReprise; } set { Set(ref _dateReprise, D(value)); } }
        public bool Mortel { get { return _mortel; } set { Set(ref _mortel, value); } }
        public DateTime? DateDeclarationCnss { get { return _dateDeclarationCnss; } set { Set(ref _dateDeclarationCnss, D(value)); } }
        public DateTime? DateDeclarationInspection { get { return _dateDeclarationInspection; } set { Set(ref _dateDeclarationInspection, D(value)); } }

        /// <summary>Référence de la déclaration ou numéro du carnet d'accident délivré par la CNSS.</summary>
        public string ReferenceCnss { get { return _referenceCnss; } set { Set(ref _referenceCnss, value); } }

        /// <summary>Rémunération journalière moyenne retenue (proposée : salaire mensuel ÷ 30).</summary>
        public decimal SalaireJournalier { get { return _salaireJournalier; } set { Set(ref _salaireJournalier, value); } }

        /// <summary>Taux d'incapacité permanente fixé par la CNSS (%), pour mémoire.</summary>
        public decimal TauxIpp { get { return _tauxIpp; } set { Set(ref _tauxIpp, value); } }

        /// <summary>Absence « Accident du travail » créée dans les congés et absences (PAI-04).</summary>
        public int? AbsenceId { get { return _absenceId; } set { Set(ref _absenceId, value); } }

        public string Commentaire { get { return _commentaire; } set { Set(ref _commentaire, value); } }

        // Suivi du remboursement des indemnités journalières par la CNSS (onglet « Indemnités
        // journalières ») : enregistré à part de la fiche, il n'est jamais écrit par elle.

        /// <summary>Date de dépôt du dossier d'indemnités journalières à la CNSS.</summary>
        public DateTime? DateDepotIJ { get { return _dateDepotIJ; } set { Set(ref _dateDepotIJ, D(value)); } }

        /// <summary>Montant déjà remboursé par la CNSS (FCFA).</summary>
        public decimal MontantRembourseIJ { get { return _montantRembourseIJ; } set { Set(ref _montantRembourseIJ, value); } }

        /// <summary>Date du (dernier) remboursement reçu de la CNSS.</summary>
        public DateTime? DateRemboursementIJ { get { return _dateRemboursementIJ; } set { Set(ref _dateRemboursementIJ, D(value)); } }

        public string NatureLibelle { get { return LibellesSST.Nature(_nature); } }
        public string DateLibelle { get { return LibellesSST.Date(_dateAccident); } }
        public bool EstNouveau { get { return _id <= 0; } }

        public string ArretLibelle
        {
            get
            {
                if (_mortel) return "Accident mortel";
                if (!_avecArret) return "Sans arrêt";
                if (!_debutArret.HasValue) return "Arrêt (dates à saisir)";
                return _finArret.HasValue
                    ? string.Format(LibellesSST.Fr, "Du {0:dd/MM} au {1:dd/MM/yyyy}", _debutArret.Value, _finArret.Value)
                    : string.Format(LibellesSST.Fr, "Depuis le {0:dd/MM/yyyy}", _debutArret.Value);
            }
        }

        public string DeclarationLibelle
        {
            get
            {
                if (_dateDeclarationCnss.HasValue && _dateDeclarationInspection.HasValue) return "Déclaré (CNSS + inspection)";
                if (_dateDeclarationCnss.HasValue) return "CNSS seule";
                if (_dateDeclarationInspection.HasValue) return "Inspection seule";
                return "À déclarer";
            }
        }

        public AccidentTravail Clone()
        {
            var c = (AccidentTravail)MemberwiseClone();
            c.PropertyChanged = null;
            return c;
        }

        private static DateTime? D(DateTime? v) { return v.HasValue ? v.Value.Date : (DateTime?)null; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T champ, T valeur, [CallerMemberName] string nom = null)
        {
            if (EqualityComparer<T>.Default.Equals(champ, valeur)) return;
            champ = valeur;
            var h = PropertyChanged;
            if (h == null) return;
            h(this, new PropertyChangedEventArgs(nom));
            h(this, new PropertyChangedEventArgs(nameof(ArretLibelle)));
            h(this, new PropertyChangedEventArgs(nameof(DeclarationLibelle)));
            h(this, new PropertyChangedEventArgs(nameof(NatureLibelle)));
        }
    }

    /// <summary>Résultat d'un contrôle SST.</summary>
    public class AlerteSST
    {
        public AlerteSST(NiveauAlerte niveau, string message) { Niveau = niveau; Message = message; }
        public NiveauAlerte Niveau { get; }
        public string Message { get; }
        public string NiveauLibelle { get { return LibellesDiscipline.Niveau(Niveau); } }
        public string Couleur { get { return Niveau == NiveauAlerte.Bloquant ? "#C62828" : Niveau == NiveauAlerte.Avertissement ? "#E65100" : "#14675A"; } }
    }

    /// <summary>Une échéance SST (visite due, EPI à renouveler, déclaration à faire).</summary>
    public class EcheanceSST
    {
        public string Categorie { get; set; }
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public string Libelle { get; set; }
        public DateTime Date { get; set; }
        public int JoursRestants { get; set; }
        public bool EstDepassee { get { return JoursRestants < 0; } }
        public string DateLibelle { get { return Date.ToString("ddd dd/MM/yyyy", LibellesSST.Fr); } }
        public string DelaiLibelle
        {
            get
            {
                if (JoursRestants == 0) return "Aujourd'hui";
                if (JoursRestants > 0) return string.Format("Dans {0} j", JoursRestants);
                return string.Format("En retard de {0} j", -JoursRestants);
            }
        }

        /// <summary>Lien vers l'objet concerné (onglet à ouvrir).</summary>
        public int? AccidentId { get; set; }
        public int? DotationId { get; set; }

        /// <summary>Visite programmée concernée, ou type de la visite à programmer.</summary>
        public int? VisiteId { get; set; }
        public TypeVisiteMedicale? TypeVisite { get; set; }
    }

    /// <summary>Situation médicale d'un salarié actif : dernière visite et prochaine échéance.</summary>
    public class SituationMedicale
    {
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public string Poste { get; set; }
        public int PeriodiciteMois { get; set; }
        public DateTime? DerniereVisite { get; set; }
        public string DerniereVisiteType { get; set; }
        public AptitudeMedicale Aptitude { get; set; }
        public string Restrictions { get; set; }
        public DateTime ProchaineEcheance { get; set; }
        public TypeVisiteMedicale TypeProchaine { get; set; }
        public bool EstProgrammee { get; set; }
        public int JoursRestants { get; set; }

        public bool EstDepassee { get { return JoursRestants < 0; } }
        public bool AvecReserves { get { return Aptitude == AptitudeMedicale.AptAvecReserves || Aptitude == AptitudeMedicale.InapteTemporaire || Aptitude == AptitudeMedicale.InapteDefinitif; } }
        public string DerniereVisiteLibelle { get { return DerniereVisite.HasValue ? LibellesSST.Date(DerniereVisite) + " (" + DerniereVisiteType + ")" : "Aucune"; } }
        public string AptitudeLibelle { get { return DerniereVisite.HasValue ? LibellesSST.Aptitude(Aptitude) : "—"; } }
        public string PeriodiciteLibelle { get { return PeriodiciteMois + " mois"; } }
        public string ProchaineLibelle
        {
            get
            {
                return LibellesSST.Date(ProchaineEcheance) + " — " + LibellesSST.Visite(TypeProchaine).Replace("Visite ", "")
                       + (EstProgrammee ? " (programmée)" : "");
            }
        }
        public string DelaiLibelle
        {
            get
            {
                if (JoursRestants == 0) return "Aujourd'hui";
                if (JoursRestants > 0) return string.Format("Dans {0} j", JoursRestants);
                return string.Format("En retard de {0} j", -JoursRestants);
            }
        }
    }

    /// <summary>Indemnités journalières d'un accident pour un mois (état à réclamer à la CNSS).</summary>
    public class LigneIndemniteJournaliere
    {
        public int AccidentId { get; set; }
        public string Reference { get; set; }
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public DateTime DateAccident { get; set; }
        public DateTime Du { get; set; }
        public DateTime Au { get; set; }
        public int Jours { get; set; }
        public decimal SalaireJournalier { get; set; }

        /// <summary>Indemnités dues par la CNSS : jours × salaire journalier × 2/3.</summary>
        public decimal MontantCnss { get; set; }

        /// <summary>Part restant à l'employeur : le tiers des jours d'arrêt.</summary>
        public decimal PartEmployeur { get; set; }

        public string PeriodeLibelle { get { return string.Format(LibellesSST.Fr, "{0:dd/MM} au {1:dd/MM/yyyy}", Du, Au); } }
        public string MoisLibelle { get { var m = Du.ToString("MMMM yyyy", LibellesSST.Fr); return char.ToUpper(m[0], LibellesSST.Fr) + m.Substring(1); } }
    }

    /// <summary>
    /// Suivi du remboursement des indemnités journalières d'un arrêt par la CNSS : montant
    /// réclamé (celui que le module calcule pour tout l'arrêt), dépôt du dossier, sommes
    /// reçues, reste dû. Le statut se déduit des montants et des dates.
    /// </summary>
    public class SuiviRemboursementIJ
    {
        public int AccidentId { get; set; }
        public string Reference { get; set; }
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public DateTime DateAccident { get; set; }

        /// <summary>Premier et dernier jour indemnisés (lendemain de l'accident → fin d'arrêt).</summary>
        public DateTime Du { get; set; }
        public DateTime Au { get; set; }

        /// <summary>Arrêt sans fin connue : les jours sont comptés jusqu'à aujourd'hui.</summary>
        public bool EnCours { get; set; }

        public int Jours { get; set; }
        public decimal SalaireJournalier { get; set; }

        /// <summary>Indemnités réclamées à la CNSS : jours × salaire journalier × 2/3.</summary>
        public decimal MontantReclame { get; set; }

        public DateTime? DateDepot { get; set; }
        public decimal MontantRembourse { get; set; }
        public DateTime? DateRemboursement { get; set; }

        public decimal RestantDu { get { return Math.Max(0m, MontantReclame - MontantRembourse); } }

        public StatutRemboursementIJ Statut
        {
            get
            {
                if (MontantRembourse > 0m)
                    return MontantRembourse >= MontantReclame ? StatutRemboursementIJ.Rembourse : StatutRemboursementIJ.RemboursePartiellement;
                return DateDepot.HasValue ? StatutRemboursementIJ.Depose : StatutRemboursementIJ.ADeposer;
            }
        }

        public string StatutLibelle { get { return LibellesSST.Remboursement(Statut); } }

        public string PeriodeLibelle
        {
            get
            {
                return string.Format(LibellesSST.Fr, "{0:dd/MM/yyyy} au {1:dd/MM/yyyy}{2}", Du, Au, EnCours ? " (en cours)" : "");
            }
        }

        public SuiviRemboursementIJ Clone() { return (SuiviRemboursementIJ)MemberwiseClone(); }
    }
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Nature d'une absence. C'est elle, et non une case à cocher, qui décide si le
    /// salarié est payé : confondre un congé payé et une absence non autorisée est une
    /// erreur qui se retrouve directement sur le net à payer.
    /// </summary>
    public enum TypeAbsence
    {
        /// <summary>Congé payé pris sur le compteur acquis (2,5 jours par mois de service).</summary>
        CongePaye = 0,

        /// <summary>Maladie justifiée par un certificat : rémunérée selon la convention.</summary>
        Maladie = 1,

        /// <summary>Congé de maternité (14 semaines, Code du travail).</summary>
        Maternite = 2,

        /// <summary>Accident du travail ou maladie professionnelle, pris en charge CNSS.</summary>
        AccidentTravail = 3,

        /// <summary>Permission exceptionnelle (mariage, décès, naissance) : rémunérée.</summary>
        PermissionExceptionnelle = 4,

        /// <summary>Absence non rémunérée : le salaire est retenu au prorata.</summary>
        AbsenceNonRemuneree = 5,

        /// <summary>Mise à pied disciplinaire : non rémunérée par définition.</summary>
        MiseAPied = 6,

        /// <summary>Chômage technique : traitement propre à l'accord d'entreprise.</summary>
        ChomageTechnique = 7
    }

    /// <summary>
    /// Base de calcul de la retenue pour absence non rémunérée. Le choix appartient à
    /// l'entreprise : la loi burkinabè ne l'impose pas, et un cabinet qui suit plusieurs
    /// dossiers rencontre les trois usages.
    /// </summary>
    public enum BaseRetenueAbsence
    {
        /// <summary>Un jour vaut le salaire mensuel divisé par 30, quel que soit le mois.</summary>
        TrentiemeMensuel = 0,

        /// <summary>Un jour vaut le salaire mensuel divisé par le nombre de jours ouvrables du mois.</summary>
        JoursOuvrablesDuMois = 1,

        /// <summary>Une heure vaut le salaire mensuel divisé par 173,33 (39 h par semaine).</summary>
        TauxHoraire = 2
    }

    /// <summary>
    /// Une absence d'un salarié sur une période : congé, maladie, permission ou absence
    /// non rémunérée (module PAI-04).
    ///
    /// Le bulletin s'appuyait jusqu'ici sur un champ « congés pris » saisi à la main,
    /// que rien ne rattachait à des dates ni à un compteur. Une absence enregistrée ici
    /// alimente les deux : le décompte des congés du bulletin, et — pour les absences
    /// non rémunérées — la retenue sur le brut.
    /// </summary>
    public class Absence : INotifyPropertyChanged
    {
        private int _id;
        private string _employeeId;
        private string _nomSalarie;
        private TypeAbsence _type = TypeAbsence.CongePaye;
        private DateTime _dateDebut = DateTime.Today;
        private DateTime _dateFin = DateTime.Today;
        private decimal _nombreJours = 1m;
        private decimal _nombreHeures;
        private string _commentaire;
        private DateTime _dateSaisie = DateTime.Now;
        private string _periodeImputation;

        public int Id
        {
            get { return _id; }
            set { Definir(ref _id, value); }
        }

        /// <summary>Matricule du salarié concerné.</summary>
        public string EmployeeId
        {
            get { return _employeeId; }
            set { Definir(ref _employeeId, value); }
        }

        /// <summary>Nom affiché, reporté depuis la fiche salarié pour la lisibilité des listes.</summary>
        public string NomSalarie
        {
            get { return _nomSalarie; }
            set
            {
                if (Definir(ref _nomSalarie, value))
                    OnPropertyChanged("Designation");
            }
        }

        public TypeAbsence Type
        {
            get { return _type; }
            set
            {
                if (Definir(ref _type, value))
                {
                    OnPropertyChanged("TypeLibelle");
                    OnPropertyChanged("EstRemuneree");
                    OnPropertyChanged("ImputeSurConges");
                    OnPropertyChanged("Designation");
                }
            }
        }

        public DateTime DateDebut
        {
            get { return _dateDebut; }
            set
            {
                if (Definir(ref _dateDebut, value))
                    OnPropertyChanged("Periode");
            }
        }

        public DateTime DateFin
        {
            get { return _dateFin; }
            set
            {
                if (Definir(ref _dateFin, value))
                    OnPropertyChanged("Periode");
            }
        }

        /// <summary>
        /// Nombre de jours décomptés. Proposé d'après les dates, mais modifiable : un
        /// dimanche au milieu d'un arrêt ne se compte pas partout de la même façon, et
        /// c'est le gestionnaire qui tranche, pas le logiciel.
        /// </summary>
        public decimal NombreJours
        {
            get { return _nombreJours; }
            set { Definir(ref _nombreJours, value); }
        }

        /// <summary>
        /// Nombre d'heures, pour les dossiers qui retiennent au taux horaire ou pour une
        /// absence de quelques heures. Zéro quand l'absence se compte en journées.
        /// </summary>
        public decimal NombreHeures
        {
            get { return _nombreHeures; }
            set { Definir(ref _nombreHeures, value); }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { Definir(ref _commentaire, value); }
        }

        public DateTime DateSaisie
        {
            get { return _dateSaisie; }
            set { Definir(ref _dateSaisie, value); }
        }

        /// <summary>
        /// Mois de paie (« yyyy-MM ») auquel l'absence est imputée EN ENTIER, quelles que
        /// soient ses dates. Renseigné par la Gestion des temps (RH-08) quand la période
        /// de pointage ne suit pas le mois civil : une absence du 25 septembre relevée sur
        /// la période du 21 septembre au 20 octobre se retient sur la paie d'octobre.
        /// Vide pour une absence saisie dans l'écran des congés : elle reste répartie sur
        /// les mois civils qu'elle touche, comme avant.
        /// </summary>
        public string PeriodeImputation
        {
            get { return _periodeImputation; }
            set { Definir(ref _periodeImputation, string.IsNullOrWhiteSpace(value) ? null : value.Trim()); }
        }

        /// <summary>Vrai si l'absence est imputée à un mois de paie précis (RH-08).</summary>
        public bool EstImputee
        {
            get { return !string.IsNullOrEmpty(_periodeImputation); }
        }

        /// <summary>Premier jour du mois d'imputation, ou null.</summary>
        public DateTime? MoisImputation
        {
            get
            {
                DateTime mois;
                if (EstImputee && DateTime.TryParseExact(_periodeImputation + "-01", "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out mois))
                    return mois;
                return null;
            }
        }

        /// <summary>Clé d'imputation d'un mois : 2026-10.</summary>
        public static string CleImputation(int annee, int mois)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:D4}-{1:D2}", annee, mois);
        }

        /// <summary>Le salarié est-il payé pendant cette absence ?</summary>
        public bool EstRemuneree
        {
            get { return EstRemunereParNature(Type); }
        }

        /// <summary>L'absence se déduit-elle du compteur de congés payés ?</summary>
        public bool ImputeSurConges
        {
            get { return Type == TypeAbsence.CongePaye; }
        }

        /// <summary>Vrai si l'absence donne lieu à une retenue sur le salaire.</summary>
        public bool DonneLieuARetenue
        {
            get { return !EstRemuneree && (NombreJours > 0m || NombreHeures > 0m); }
        }

        public string TypeLibelle
        {
            get { return Libelle(Type); }
        }

        public string Periode
        {
            get
            {
                if (DateDebut.Date == DateFin.Date)
                    return DateDebut.ToString("dd/MM/yyyy");

                return string.Format("{0:dd/MM/yyyy} au {1:dd/MM/yyyy}", DateDebut, DateFin);
            }
        }

        public string Designation
        {
            get { return string.Format("{0} — {1}", NomSalarie, TypeLibelle); }
        }

        /// <summary>
        /// Types rémunérés. Le chômage technique et la mise à pied sont hors de cette
        /// liste : le premier relève d'un accord d'entreprise, le second est par nature
        /// une privation de salaire.
        /// </summary>
        public static bool EstRemunereParNature(TypeAbsence type)
        {
            switch (type)
            {
                case TypeAbsence.CongePaye:
                case TypeAbsence.Maladie:
                case TypeAbsence.Maternite:
                case TypeAbsence.AccidentTravail:
                case TypeAbsence.PermissionExceptionnelle:
                    return true;

                default:
                    return false;
            }
        }

        public static string Libelle(TypeAbsence type)
        {
            switch (type)
            {
                case TypeAbsence.CongePaye: return "Congé payé";
                case TypeAbsence.Maladie: return "Maladie";
                case TypeAbsence.Maternite: return "Maternité";
                case TypeAbsence.AccidentTravail: return "Accident du travail";
                case TypeAbsence.PermissionExceptionnelle: return "Permission exceptionnelle";
                case TypeAbsence.AbsenceNonRemuneree: return "Absence non rémunérée";
                case TypeAbsence.MiseAPied: return "Mise à pied";
                case TypeAbsence.ChomageTechnique: return "Chômage technique";
                default: return type.ToString();
            }
        }

        /// <summary>
        /// Nombre de jours entre deux dates, bornes comprises. Les jours ouvrables se
        /// comptent du lundi au samedi, comme le retient le Code du travail burkinabè
        /// pour les congés payés — le dimanche et lui seul est écarté.
        /// </summary>
        public static decimal JoursEntre(DateTime debut, DateTime fin, bool ouvrablesSeulement)
        {
            if (fin.Date < debut.Date) return 0m;

            int total = 0;
            for (var jour = debut.Date; jour <= fin.Date; jour = jour.AddDays(1))
            {
                if (ouvrablesSeulement && jour.DayOfWeek == DayOfWeek.Sunday)
                    continue;

                total++;
            }

            return total;
        }

        public Absence Clone()
        {
            return new Absence
            {
                Id = Id,
                EmployeeId = EmployeeId,
                NomSalarie = NomSalarie,
                Type = Type,
                DateDebut = DateDebut,
                DateFin = DateFin,
                NombreJours = NombreJours,
                NombreHeures = NombreHeures,
                Commentaire = Commentaire,
                DateSaisie = DateSaisie,
                PeriodeImputation = PeriodeImputation
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Definir<T>(ref T champ, T valeur, [CallerMemberName] string nom = null)
        {
            if (Equals(champ, valeur)) return false;
            champ = valeur;
            OnPropertyChanged(nom);
            return true;
        }

        private void OnPropertyChanged(string nom)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(nom));
        }
    }

    /// <summary>
    /// Situation d'un salarié au regard de ses congés payés, à une date donnée : ce qu'il
    /// a acquis depuis son entrée, ce qu'il a pris, ce qui lui reste.
    /// </summary>
    public class SoldeConges
    {
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }

        /// <summary>Mois de service effectif comptés à la date d'arrêté.</summary>
        public int MoisDeService { get; set; }

        /// <summary>Jours acquis depuis l'entrée : mois de service × droit mensuel.</summary>
        public decimal JoursAcquis { get; set; }

        /// <summary>Jours de congé payé pris, toutes périodes confondues.</summary>
        public decimal JoursPris { get; set; }

        /// <summary>Jours de congé payé pris sur la seule période de paie en cours.</summary>
        public decimal JoursPrisPeriode { get; set; }

        /// <summary>Jours d'absence non rémunérée sur la période de paie en cours.</summary>
        public decimal JoursNonRemuneresPeriode { get; set; }

        /// <summary>Reliquat : acquis moins pris. Peut être négatif si l'entreprise a avancé des congés.</summary>
        public decimal Solde
        {
            get { return JoursAcquis - JoursPris; }
        }

        /// <summary>Vrai si le salarié a pris plus de congés qu'il n'en a acquis.</summary>
        public bool SoldeNegatif
        {
            get { return Solde < 0m; }
        }

        public string SoldeLibelle
        {
            get
            {
                return Solde < 0m
                    ? string.Format("{0:0.##} j (avance)", Math.Abs(Solde))
                    : string.Format("{0:0.##} j", Solde);
            }
        }
    }
}

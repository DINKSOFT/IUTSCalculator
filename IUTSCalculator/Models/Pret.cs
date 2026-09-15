using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Nature de la somme avancée au salarié. Les trois se remboursent de la même
    /// façon — une retenue sur le net — mais ne se justifient pas pareil devant un
    /// contrôle, et le salarié n'attend pas le même libellé sur son bulletin.
    /// </summary>
    public enum TypePret
    {
        /// <summary>Prêt consenti par l'entreprise, remboursé sur plusieurs mois.</summary>
        Pret = 0,

        /// <summary>Avance sur salaire : somme versée d'avance, récupérée en une ou plusieurs fois.</summary>
        AvanceSurSalaire = 1,

        /// <summary>Acompte : fraction du salaire du mois versée avant l'échéance de paie.</summary>
        Acompte = 2
    }

    /// <summary>
    /// État d'un prêt tel que le gestionnaire l'a décidé. « Soldé » n'y figure pas :
    /// il ne se décide pas, il se constate — voir <see cref="SituationPret.EstSolde"/>.
    /// Deux sources de vérité pour le même fait finissent toujours par diverger.
    /// </summary>
    public enum StatutPret
    {
        /// <summary>Le remboursement suit son échéancier.</summary>
        EnCours = 0,

        /// <summary>Remboursement interrompu : aucune retenue n'est opérée tant qu'il dure.</summary>
        Suspendu = 1,

        /// <summary>Prêt annulé : abandon de créance, erreur de saisie, départ du salarié.</summary>
        Annule = 2
    }

    /// <summary>État d'une échéance de l'échéancier.</summary>
    public enum StatutEcheance
    {
        /// <summary>Échéance à retenir sur le bulletin de son mois.</summary>
        Prevue = 0,

        /// <summary>Échéance sautée : le mois passe sans retenue, le solde ne bouge pas.</summary>
        Suspendue = 1,

        /// <summary>Échéance supprimée de l'échéancier.</summary>
        Annulee = 2
    }

    /// <summary>
    /// Un prêt, une avance ou un acompte accordé à un salarié (module PAI-05).
    ///
    /// C'est l'un des besoins les plus courants en PME burkinabè, et il était jusqu'ici
    /// totalement absent : la seule façon de retenir un remboursement était de saisir
    /// une retenue à la main dans les variables du mois, chaque mois, sans échéancier,
    /// sans solde, et sans que rien ne signale l'oubli du mois suivant ni la retenue de
    /// trop après le dernier versement.
    ///
    /// Le prêt porte l'accord ; l'échéancier (<see cref="EcheancePret"/>) porte le
    /// calendrier. La retenue du mois se lit dans l'échéancier, elle ne se recalcule pas
    /// à la volée : un remboursement suspendu un mois puis repris décale les échéances
    /// suivantes, et seul un calendrier écrit garde la trace de ce décalage.
    /// </summary>
    public class Pret : INotifyPropertyChanged
    {
        private int _id;
        private string _employeeId;
        private string _nomSalarie;
        private TypePret _type = TypePret.Pret;
        private string _reference;
        private DateTime _dateOctroi = DateTime.Today;
        private decimal _montantAccorde;
        private decimal _montantEcheance;
        private int _nombreEcheances = 1;
        private int _anneePremiereEcheance = DateTime.Today.Year;
        private int _moisPremiereEcheance = DateTime.Today.Month;
        private StatutPret _statut = StatutPret.EnCours;
        private string _motif;
        private string _commentaire;
        private DateTime _dateSaisie = DateTime.Now;

        public int Id
        {
            get { return _id; }
            set { Definir(ref _id, value); }
        }

        /// <summary>Matricule du salarié bénéficiaire.</summary>
        public string EmployeeId
        {
            get { return _employeeId; }
            set { Definir(ref _employeeId, value); }
        }

        /// <summary>Nom affiché, repris de la fiche salarié. Il n'est pas stocké : il est joint.</summary>
        public string NomSalarie
        {
            get { return _nomSalarie; }
            set
            {
                if (Definir(ref _nomSalarie, value))
                    OnPropertyChanged("Designation");
            }
        }

        public TypePret Type
        {
            get { return _type; }
            set
            {
                if (Definir(ref _type, value))
                {
                    OnPropertyChanged("TypeLibelle");
                    OnPropertyChanged("Designation");
                }
            }
        }

        /// <summary>Référence de la convention ou du bon d'avance, pour retrouver la pièce.</summary>
        public string Reference
        {
            get { return _reference; }
            set { Definir(ref _reference, value); }
        }

        public DateTime DateOctroi
        {
            get { return _dateOctroi; }
            set { Definir(ref _dateOctroi, value); }
        }

        /// <summary>Somme remise au salarié. C'est elle, et rien d'autre, qui doit être remboursée.</summary>
        public decimal MontantAccorde
        {
            get { return _montantAccorde; }
            set
            {
                if (Definir(ref _montantAccorde, value))
                    OnPropertyChanged("Designation");
            }
        }

        /// <summary>
        /// Retenue mensuelle prévue. La dernière échéance est ajustée au franc près pour
        /// que la somme de l'échéancier égale exactement le montant accordé : un arrondi
        /// répété douze fois finit par retenir quelques centaines de francs de trop.
        /// </summary>
        public decimal MontantEcheance
        {
            get { return _montantEcheance; }
            set { Definir(ref _montantEcheance, value); }
        }

        public int NombreEcheances
        {
            get { return _nombreEcheances; }
            set { Definir(ref _nombreEcheances, value); }
        }

        public int AnneePremiereEcheance
        {
            get { return _anneePremiereEcheance; }
            set
            {
                if (Definir(ref _anneePremiereEcheance, value))
                    OnPropertyChanged("PremiereEcheanceLibelle");
            }
        }

        /// <summary>Mois de 1 à 12 de la première retenue.</summary>
        public int MoisPremiereEcheance
        {
            get { return _moisPremiereEcheance; }
            set
            {
                if (Definir(ref _moisPremiereEcheance, value))
                    OnPropertyChanged("PremiereEcheanceLibelle");
            }
        }

        public StatutPret Statut
        {
            get { return _statut; }
            set
            {
                if (Definir(ref _statut, value))
                {
                    OnPropertyChanged("StatutLibelle");
                    OnPropertyChanged("EstActif");
                }
            }
        }

        /// <summary>Motif de l'avance, ou motif de la suspension quand le prêt est interrompu.</summary>
        public string Motif
        {
            get { return _motif; }
            set { Definir(ref _motif, value); }
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

        // ── Dérivés ──────────────────────────────────────────────────────────

        /// <summary>Vrai si le prêt donne lieu à une retenue ce mois-ci.</summary>
        public bool EstActif
        {
            get { return Statut == StatutPret.EnCours; }
        }

        public string TypeLibelle
        {
            get { return Libelle(Type); }
        }

        public string StatutLibelle
        {
            get { return Libelle(Statut); }
        }

        /// <summary>« Mars 2026 », première retenue prévue.</summary>
        public string PremiereEcheanceLibelle
        {
            get { return LibellePeriode(AnneePremiereEcheance, MoisPremiereEcheance); }
        }

        public string Designation
        {
            get
            {
                return string.Format(
                    CultureInfo.CurrentCulture, "{0} — {1} de {2:N0}",
                    NomSalarie, TypeLibelle, MontantAccorde);
            }
        }

        public static string Libelle(TypePret type)
        {
            switch (type)
            {
                case TypePret.Pret: return "Prêt";
                case TypePret.AvanceSurSalaire: return "Avance sur salaire";
                case TypePret.Acompte: return "Acompte";
                default: return type.ToString();
            }
        }

        public static string Libelle(StatutPret statut)
        {
            switch (statut)
            {
                case StatutPret.EnCours: return "En cours";
                case StatutPret.Suspendu: return "Suspendu";
                case StatutPret.Annule: return "Annulé";
                default: return statut.ToString();
            }
        }

        /// <summary>
        /// Code porté par la ligne de retenue du bulletin. Il ne renvoie à AUCUNE rubrique
        /// du plan de paie : la retenue vient de ce module, pas d'une saisie. Lui donner
        /// le code d'une rubrique saisissable exposerait au doublon — la retenue
        /// automatique plus celle que le gestionnaire aurait saisie par habitude.
        /// </summary>
        public static string CodeRetenue(TypePret type)
        {
            switch (type)
            {
                case TypePret.AvanceSurSalaire: return "AVANCE";
                case TypePret.Acompte: return "ACOMPTE";
                default: return "PRET";
            }
        }

        /// <summary>« Mars 2026 », avec la majuscule initiale attendue en français.</summary>
        public static string LibellePeriode(int annee, int mois)
        {
            if (mois < 1 || mois > 12) return annee.ToString(CultureInfo.InvariantCulture);

            var culture = CultureInfo.GetCultureInfo("fr-FR");
            string nom = culture.DateTimeFormat.GetMonthName(mois);
            if (nom.Length > 0)
                nom = char.ToUpper(nom[0], culture) + nom.Substring(1);

            return string.Format("{0} {1}", nom, annee);
        }

        /// <summary>
        /// Construit l'échéancier complet à partir de l'accord : autant d'échéances que
        /// prévu, à partir du mois de la première retenue, mois par mois.
        ///
        /// La dernière échéance absorbe l'écart d'arrondi. C'est ce qui garantit
        /// l'égalité stricte entre la somme des échéances et le montant accordé : un
        /// prêt de 100 000 remboursé en trois fois ne peut pas se retenir 3 × 33 334.
        /// </summary>
        public List<EcheancePret> ConstruireEcheancier()
        {
            return ConstruireEcheancier(
                MontantAccorde, MontantEcheance, NombreEcheances,
                AnneePremiereEcheance, MoisPremiereEcheance, 1);
        }

        /// <summary>
        /// Échéancier d'un reliquat : sert à la reprise après suspension, où il reste un
        /// solde à étaler à partir d'un nouveau mois, sans toucher à ce qui a déjà été
        /// retenu.
        /// </summary>
        public static List<EcheancePret> ConstruireEcheancier(
            decimal montantTotal, decimal montantEcheance, int nombreEcheances,
            int annee, int mois, int rangInitial)
        {
            var echeancier = new List<EcheancePret>();

            if (montantTotal <= 0m) return echeancier;
            if (mois < 1 || mois > 12) return echeancier;

            if (nombreEcheances <= 0)
            {
                nombreEcheances = montantEcheance > 0m
                    ? (int)Math.Ceiling(montantTotal / montantEcheance)
                    : 1;
            }

            if (nombreEcheances <= 0) nombreEcheances = 1;

            // L'échéance retenue est arrondie au franc : une retenue au centime sur un
            // bulletin burkinabè n'a pas de sens, la monnaie n'en a pas.
            decimal echeance = montantEcheance > 0m
                ? Math.Round(montantEcheance, 0, MidpointRounding.AwayFromZero)
                : Math.Round(montantTotal / nombreEcheances, 0, MidpointRounding.AwayFromZero);

            if (echeance <= 0m) echeance = montantTotal;

            var debut = new DateTime(annee, mois, 1);
            decimal reste = montantTotal;

            for (int rang = 0; rang < nombreEcheances && reste > 0m; rang++)
            {
                var moisEcheance = debut.AddMonths(rang);
                bool derniere = (rang == nombreEcheances - 1);

                decimal montant = derniere || echeance > reste ? reste : echeance;

                echeancier.Add(new EcheancePret
                {
                    Rang = rangInitial + rang,
                    Annee = moisEcheance.Year,
                    Mois = moisEcheance.Month,
                    MontantPrevu = montant,
                    Statut = StatutEcheance.Prevue
                });

                reste -= montant;
            }

            // Le reliquat d'un échéancier trop court rejoint la dernière échéance plutôt
            // que de disparaître : un solde qui s'évapore est une créance perdue.
            if (reste > 0m && echeancier.Count > 0)
                echeancier[echeancier.Count - 1].MontantPrevu += reste;

            return echeancier;
        }

        public Pret Clone()
        {
            return new Pret
            {
                Id = Id,
                EmployeeId = EmployeeId,
                NomSalarie = NomSalarie,
                Type = Type,
                Reference = Reference,
                DateOctroi = DateOctroi,
                MontantAccorde = MontantAccorde,
                MontantEcheance = MontantEcheance,
                NombreEcheances = NombreEcheances,
                AnneePremiereEcheance = AnneePremiereEcheance,
                MoisPremiereEcheance = MoisPremiereEcheance,
                Statut = Statut,
                Motif = Motif,
                Commentaire = Commentaire,
                DateSaisie = DateSaisie
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
    /// Une échéance de remboursement : le mois où elle tombe et le montant à retenir.
    ///
    /// Elle n'a pas d'état « payée ». Une échéance est réputée retenue quand la PÉRIODE
    /// à laquelle elle se rapporte est clôturée — c'est la règle du projet : les totaux
    /// d'une période ouverte se recalculent à chaque lecture, ceux d'une période close
    /// restent figés. Écrire un état « payée » en plus de la clôture donnerait deux
    /// vérités pour le même mois, et rien ne dirait laquelle croire après une réouverture.
    /// </summary>
    public class EcheancePret : INotifyPropertyChanged
    {
        private int _id;
        private int _pretId;
        private int _rang;
        private int _annee;
        private int _mois;
        private decimal _montantPrevu;
        private StatutEcheance _statut = StatutEcheance.Prevue;
        private string _commentaire;

        private bool _periodeCloturee;

        public int Id
        {
            get { return _id; }
            set { Definir(ref _id, value); }
        }

        public int PretId
        {
            get { return _pretId; }
            set { Definir(ref _pretId, value); }
        }

        /// <summary>Numéro d'ordre dans l'échéancier, à partir de 1.</summary>
        public int Rang
        {
            get { return _rang; }
            set { Definir(ref _rang, value); }
        }

        public int Annee
        {
            get { return _annee; }
            set
            {
                if (Definir(ref _annee, value))
                    OnPropertyChanged("PeriodeLibelle");
            }
        }

        /// <summary>Mois de 1 à 12.</summary>
        public int Mois
        {
            get { return _mois; }
            set
            {
                if (Definir(ref _mois, value))
                    OnPropertyChanged("PeriodeLibelle");
            }
        }

        public decimal MontantPrevu
        {
            get { return _montantPrevu; }
            set { Definir(ref _montantPrevu, value); }
        }

        public StatutEcheance Statut
        {
            get { return _statut; }
            set
            {
                if (Definir(ref _statut, value))
                {
                    OnPropertyChanged("StatutLibelle");
                    OnPropertyChanged("EstRetenue");
                    OnPropertyChanged("EtatLibelle");
                }
            }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { Definir(ref _commentaire, value); }
        }

        /// <summary>
        /// La période de cette échéance est-elle clôturée ? Renseigné à la lecture, à
        /// partir des périodes de paie (PAI-01). Ce n'est pas une colonne de la table :
        /// une période rouverte redeviendrait aussitôt modifiable, et une copie figée
        /// ici prétendrait le contraire.
        /// </summary>
        public bool PeriodeCloturee
        {
            get { return _periodeCloturee; }
            set
            {
                if (Definir(ref _periodeCloturee, value))
                {
                    OnPropertyChanged("EstRetenue");
                    OnPropertyChanged("EtatLibelle");
                }
            }
        }

        /// <summary>
        /// Vrai quand l'échéance a réellement été retenue : elle était prévue, et son
        /// mois est arrêté. C'est cette seule propriété qui alimente le solde restant dû.
        /// </summary>
        public bool EstRetenue
        {
            get { return Statut == StatutEcheance.Prevue && PeriodeCloturee; }
        }

        /// <summary>Vrai si l'échéance reste à retenir sur un mois encore ouvert.</summary>
        public bool EstAVenir
        {
            get { return Statut == StatutEcheance.Prevue && !PeriodeCloturee; }
        }

        public string PeriodeLibelle
        {
            get { return Pret.LibellePeriode(Annee, Mois); }
        }

        public string StatutLibelle
        {
            get
            {
                switch (Statut)
                {
                    case StatutEcheance.Suspendue: return "Suspendue";
                    case StatutEcheance.Annulee: return "Annulée";
                    default: return "Prévue";
                }
            }
        }

        /// <summary>Ce que le gestionnaire a besoin de lire d'un coup d'œil sur la ligne.</summary>
        public string EtatLibelle
        {
            get
            {
                if (Statut != StatutEcheance.Prevue) return StatutLibelle;
                return PeriodeCloturee ? "Retenue" : "À retenir";
            }
        }

        public EcheancePret Clone()
        {
            return new EcheancePret
            {
                Id = Id,
                PretId = PretId,
                Rang = Rang,
                Annee = Annee,
                Mois = Mois,
                MontantPrevu = MontantPrevu,
                Statut = Statut,
                Commentaire = Commentaire,
                PeriodeCloturee = PeriodeCloturee
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
    /// Situation d'un prêt à une date : ce qui a été accordé, ce qui a été retenu, ce
    /// qui reste dû. C'est la réponse à la seule question que pose un salarié — « il me
    /// reste combien ? » — et celle que pose le comptable au moment de l'inventaire.
    /// </summary>
    public class SituationPret
    {
        public int PretId { get; set; }
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public TypePret Type { get; set; }
        public StatutPret Statut { get; set; }
        public string Reference { get; set; }

        public decimal MontantAccorde { get; set; }
        public decimal MontantEcheance { get; set; }

        /// <summary>Somme des échéances retenues, c'est-à-dire tombées sur un mois clôturé.</summary>
        public decimal TotalRetenu { get; set; }

        /// <summary>Échéances prévues sur des mois encore ouverts.</summary>
        public decimal TotalAVenir { get; set; }

        public int EcheancesTotal { get; set; }
        public int EcheancesRetenues { get; set; }

        /// <summary>Retenue portée sur le bulletin de la période consultée.</summary>
        public decimal RetenueDuMois { get; set; }

        /// <summary>Période de la prochaine échéance à retenir, vide s'il n'en reste aucune.</summary>
        public string ProchaineEcheance { get; set; }

        public string TypeLibelle
        {
            get { return Pret.Libelle(Type); }
        }

        /// <summary>Reste dû : accordé moins ce qui a réellement été retenu.</summary>
        public decimal SoldeRestantDu
        {
            get
            {
                decimal solde = MontantAccorde - TotalRetenu;
                return solde < 0m ? 0m : solde;
            }
        }

        public int EcheancesRestantes
        {
            get
            {
                int reste = EcheancesTotal - EcheancesRetenues;
                return reste < 0 ? 0 : reste;
            }
        }

        /// <summary>Prêt remboursé : il ne reste rien à retenir.</summary>
        public bool EstSolde
        {
            get { return SoldeRestantDu <= 0m; }
        }

        /// <summary>
        /// Ce qui reste dû n'est couvert par aucune échéance à venir. Le cas se produit
        /// après une suspension dont on n'a pas programmé la reprise : le prêt dort, le
        /// solde ne bouge plus, et personne ne s'en aperçoit avant l'inventaire.
        /// </summary>
        public bool SansEcheancierRestant
        {
            get { return !EstSolde && TotalAVenir <= 0m && Statut != StatutPret.Annule; }
        }

        public string StatutLibelle
        {
            get
            {
                if (Statut == StatutPret.Annule) return "Annulé";
                if (EstSolde) return "Soldé";
                if (Statut == StatutPret.Suspendu) return "Suspendu";
                return "En cours";
            }
        }

        public string AvancementLibelle
        {
            get
            {
                return string.Format(
                    "{0} / {1} échéance(s)", EcheancesRetenues, EcheancesTotal);
            }
        }
    }
}

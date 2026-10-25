using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Module RH-06 — Discipline & procédures
    //
    //  Le Code du travail pose le cadre (discipline de l'entreprise, faute lourde
    //  privative de préavis et d'indemnité, motif écrit obligatoire de la rupture,
    //  autorisation de l'inspecteur du travail pour un délégué du personnel) mais
    //  renvoie l'échelle des sanctions et les délais de procédure au règlement
    //  intérieur visé par l'inspection et à la convention collective. D'où un module
    //  PARAMÉTRABLE : le catalogue des sanctions et les délais sont ceux du dossier,
    //  avec des valeurs par défaut indicatives.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Famille d'une sanction : elle décide de son effet sur la paie et sur le contrat.</summary>
    public enum NatureSanction
    {
        /// <summary>Avertissement, blâme : sanction morale, sans effet sur le salaire.</summary>
        Morale = 0,

        /// <summary>Mise à pied disciplinaire : suspension du contrat ET du salaire.</summary>
        MiseAPied = 1,

        /// <summary>Licenciement disciplinaire : rupture du contrat (solde de tout compte SAL-05).</summary>
        Licenciement = 2,

        /// <summary>Autre mesure prévue au règlement intérieur (rétrogradation, mutation…).</summary>
        Autre = 3
    }

    /// <summary>Qualification de la faute retenue par l'employeur.</summary>
    public enum GraviteFaute
    {
        Legere = 0,
        Serieuse = 1,

        /// <summary>Faute lourde : seule à autoriser la rupture sans préavis ni indemnité de licenciement.</summary>
        Lourde = 2
    }

    /// <summary>Réponse de l'inspecteur du travail à une demande d'autorisation de licencier un salarié protégé.</summary>
    public enum DecisionInspection
    {
        SansObjet = 0,
        EnAttente = 1,
        Accordee = 2,
        Refusee = 3
    }

    /// <summary>Étape atteinte par une procédure, déduite de ce qui a été saisi.</summary>
    public enum StatutProcedure
    {
        Ouverte = 0,
        ExplicationDemandee = 1,
        EntretienPrevu = 2,
        DecisionPrise = 3,
        Notifiee = 4,
        ClasseeSansSuite = 5
    }

    public enum NiveauAlerte
    {
        Info = 0,
        Avertissement = 1,

        /// <summary>Empêche l'enregistrement : la saisie contredit une règle de procédure.</summary>
        Bloquant = 2
    }

    /// <summary>Nature d'une pièce du dossier disciplinaire.</summary>
    public enum TypePiece
    {
        ConstatFaits = 0,
        Temoignage = 1,
        DemandeExplication = 2,
        ReponseSalarie = 3,
        Convocation = 4,
        CompteRenduEntretien = 5,
        AutorisationInspection = 6,
        Notification = 7,
        AccuseReception = 8,
        MiseAPiedConservatoire = 9,
        Autre = 10
    }

    /// <summary>Libellés partagés par l'écran, les lettres et l'export.</summary>
    public static class LibellesDiscipline
    {
        public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static string Nature(NatureSanction n)
        {
            switch (n)
            {
                case NatureSanction.Morale: return "Sanction morale";
                case NatureSanction.MiseAPied: return "Mise à pied";
                case NatureSanction.Licenciement: return "Licenciement";
                default: return "Autre mesure";
            }
        }

        public static string Gravite(GraviteFaute g)
        {
            switch (g)
            {
                case GraviteFaute.Legere: return "Faute légère";
                case GraviteFaute.Serieuse: return "Faute sérieuse";
                default: return "Faute lourde";
            }
        }

        public static string Statut(StatutProcedure s)
        {
            switch (s)
            {
                case StatutProcedure.Ouverte: return "Ouverte";
                case StatutProcedure.ExplicationDemandee: return "Explication demandée";
                case StatutProcedure.EntretienPrevu: return "Entretien préalable";
                case StatutProcedure.DecisionPrise: return "Décision à notifier";
                case StatutProcedure.Notifiee: return "Notifiée";
                default: return "Classée sans suite";
            }
        }

        public static string Inspection(DecisionInspection d)
        {
            switch (d)
            {
                case DecisionInspection.EnAttente: return "Demande en attente";
                case DecisionInspection.Accordee: return "Autorisation accordée";
                case DecisionInspection.Refusee: return "Autorisation refusée";
                default: return "Sans objet";
            }
        }

        public static string Piece(TypePiece t)
        {
            switch (t)
            {
                case TypePiece.ConstatFaits: return "Rapport / constat des faits";
                case TypePiece.Temoignage: return "Témoignage";
                case TypePiece.DemandeExplication: return "Demande d'explication";
                case TypePiece.ReponseSalarie: return "Réponse écrite du salarié";
                case TypePiece.Convocation: return "Convocation à l'entretien préalable";
                case TypePiece.CompteRenduEntretien: return "Compte rendu d'entretien";
                case TypePiece.AutorisationInspection: return "Autorisation de l'inspecteur du travail";
                case TypePiece.Notification: return "Notification de la décision";
                case TypePiece.AccuseReception: return "Décharge / accusé de réception";
                case TypePiece.MiseAPiedConservatoire: return "Notification de mise à pied conservatoire";
                default: return "Autre pièce";
            }
        }

        public static string Niveau(NiveauAlerte n)
        {
            switch (n)
            {
                case NiveauAlerte.Bloquant: return "Bloquant";
                case NiveauAlerte.Avertissement: return "À vérifier";
                default: return "Info";
            }
        }

        /// <summary>Catégories de faits proposées à la saisie (texte libre accepté).</summary>
        public static readonly IReadOnlyList<string> CategoriesFaits = new[]
        {
            "Absence injustifiée",
            "Retards répétés",
            "Abandon de poste",
            "Insubordination / refus d'exécuter un ordre",
            "Négligence professionnelle",
            "Comportement / propos injurieux",
            "Violence",
            "Non-respect des règles d'hygiène et de sécurité",
            "Vol, fraude, détournement",
            "Ivresse ou consommation de stupéfiants au travail",
            "Manquement à la confidentialité / loyauté",
            "Autre manquement au règlement intérieur"
        };

        /// <summary>Modes de notification d'une décision écrite.</summary>
        public static readonly IReadOnlyList<string> ModesNotification = new[]
        {
            "Remise en main propre contre décharge",
            "Lettre recommandée avec accusé de réception",
            "Exploit d'huissier",
            "Refus de signer constaté par deux témoins"
        };

        public static string Date(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("dd/MM/yyyy", Fr) : "—";
        }

        public static string DateLongue(DateTime d)
        {
            return d.ToString("dd MMMM yyyy", Fr);
        }
    }

    /// <summary>
    /// Une sanction du catalogue de l'entreprise (règlement intérieur). Les exigences de
    /// procédure (explication écrite, entretien préalable) et la durée maximale de mise à
    /// pied sont portées par la sanction : c'est elle que le moteur de contrôle consulte.
    /// </summary>
    public class TypeSanction : INotifyPropertyChanged
    {
        private int _id;
        private string _code;
        private string _libelle;
        private NatureSanction _nature;
        private int _ordre;
        private bool _exigeExplication = true;
        private bool _exigeEntretien;
        private int _dureeMaxJours;
        private bool _fauteLourde;
        private int _effacementMois;
        private bool _actif = true;
        private bool _estSysteme;

        public int Id { get { return _id; } set { Definir(ref _id, value); } }

        public string Code
        {
            get { return _code; }
            set { Definir(ref _code, string.IsNullOrWhiteSpace(value) ? value : value.Trim().ToUpperInvariant()); }
        }

        public string Libelle { get { return _libelle; } set { if (Definir(ref _libelle, value)) OnPropertyChanged(nameof(Designation)); } }

        public NatureSanction Nature
        {
            get { return _nature; }
            set
            {
                if (Definir(ref _nature, value))
                {
                    OnPropertyChanged(nameof(NatureLibelle));
                    OnPropertyChanged(nameof(EstMiseAPied));
                    OnPropertyChanged(nameof(EstLicenciement));
                }
            }
        }

        /// <summary>Rang dans l'échelle des sanctions (1 = la plus légère).</summary>
        public int Ordre { get { return _ordre; } set { Definir(ref _ordre, value); } }

        /// <summary>Une demande d'explication écrite doit précéder la décision.</summary>
        public bool ExigeExplication { get { return _exigeExplication; } set { Definir(ref _exigeExplication, value); } }

        /// <summary>Un entretien préalable (convocation, assistance) doit précéder la décision.</summary>
        public bool ExigeEntretien { get { return _exigeEntretien; } set { Definir(ref _exigeEntretien, value); } }

        /// <summary>Mise à pied : nombre de jours maximal (0 = non contrôlé).</summary>
        public int DureeMaxJours { get { return _dureeMaxJours; } set { Definir(ref _dureeMaxJours, value < 0 ? 0 : value); } }

        /// <summary>Licenciement pour faute lourde : sans préavis ni indemnité de licenciement.</summary>
        public bool FauteLourde { get { return _fauteLourde; } set { Definir(ref _fauteLourde, value); } }

        /// <summary>
        /// Délai au-delà duquel la sanction n'est plus invoquée comme antécédent (0 = jamais
        /// effacée). Valeur du règlement intérieur ou de la convention.
        /// </summary>
        public int EffacementMois { get { return _effacementMois; } set { Definir(ref _effacementMois, value < 0 ? 0 : value); } }

        public bool Actif { get { return _actif; } set { Definir(ref _actif, value); } }

        /// <summary>Sanction livrée avec le module : modifiable, mais pas supprimable.</summary>
        public bool EstSysteme { get { return _estSysteme; } set { Definir(ref _estSysteme, value); } }

        public string NatureLibelle { get { return LibellesDiscipline.Nature(_nature); } }
        public bool EstMiseAPied { get { return _nature == NatureSanction.MiseAPied; } }
        public bool EstLicenciement { get { return _nature == NatureSanction.Licenciement; } }

        public string Designation
        {
            get { return string.IsNullOrWhiteSpace(_code) ? _libelle : string.Format("{0} — {1}", _code, _libelle); }
        }

        /// <summary>Motif de départ SAL-05 qu'entraîne un licenciement de ce type ; null sinon.</summary>
        public MotifDepart? MotifDepartAssocie
        {
            get
            {
                if (_nature != NatureSanction.Licenciement) return null;
                return _fauteLourde ? MotifDepart.LicenciementFauteLourde : MotifDepart.LicenciementPersonnel;
            }
        }

        public TypeSanction Clone()
        {
            return (TypeSanction)MemberwiseCloneSansAbonnes();
        }

        private object MemberwiseCloneSansAbonnes()
        {
            var copie = (TypeSanction)MemberwiseClone();
            copie.PropertyChanged = null;
            return copie;
        }

        /// <summary>
        /// Catalogue livré : l'échelle la plus répandue dans les règlements intérieurs du
        /// secteur privé. Indicatif — chaque dossier l'aligne sur son règlement visé.
        /// </summary>
        public static List<TypeSanction> CatalogueParDefaut()
        {
            return new List<TypeSanction>
            {
                new TypeSanction { Code = "AVT", Libelle = "Avertissement écrit", Nature = NatureSanction.Morale, Ordre = 1,
                    ExigeExplication = true, EffacementMois = 12, EstSysteme = true },
                new TypeSanction { Code = "BLA", Libelle = "Blâme", Nature = NatureSanction.Morale, Ordre = 2,
                    ExigeExplication = true, EffacementMois = 24, EstSysteme = true },
                new TypeSanction { Code = "MAP", Libelle = "Mise à pied disciplinaire sans salaire", Nature = NatureSanction.MiseAPied, Ordre = 3,
                    ExigeExplication = true, DureeMaxJours = 8, EffacementMois = 36, EstSysteme = true },
                new TypeSanction { Code = "LIC", Libelle = "Licenciement pour motif disciplinaire", Nature = NatureSanction.Licenciement, Ordre = 4,
                    ExigeExplication = true, ExigeEntretien = true, EstSysteme = true },
                new TypeSanction { Code = "LFL", Libelle = "Licenciement pour faute lourde", Nature = NatureSanction.Licenciement, Ordre = 5,
                    ExigeExplication = true, ExigeEntretien = true, FauteLourde = true, EstSysteme = true }
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Definir<T>(ref T champ, T valeur, [CallerMemberName] string nom = null)
        {
            if (EqualityComparer<T>.Default.Equals(champ, valeur)) return false;
            champ = valeur;
            OnPropertyChanged(nom);
            return true;
        }

        private void OnPropertyChanged(string nom)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(nom));
        }
    }

    /// <summary>
    /// Délais de procédure du dossier. Valeurs par défaut INDICATIVES : le Code du travail
    /// ne les fixe pas ; elles s'alignent sur le règlement intérieur et la convention.
    /// </summary>
    public class ParametresDiscipline
    {
        /// <summary>Jours (calendaires) entre la connaissance des faits et le premier acte de procédure.</summary>
        public int DelaiEngagementJours { get; set; } = 60;

        /// <summary>Jours ouvrables laissés au salarié pour répondre à la demande d'explication.</summary>
        public int DelaiReponseJoursOuvrables { get; set; } = 3;

        /// <summary>Jours ouvrables minimum entre la convocation et l'entretien préalable.</summary>
        public int DelaiConvocationJoursOuvrables { get; set; } = 5;

        /// <summary>Jours (calendaires) pour décider après la réponse ou l'entretien.</summary>
        public int DelaiDecisionJours { get; set; } = 30;

        /// <summary>Référence du règlement intérieur, citée dans les lettres (visa de l'inspection).</summary>
        public string ReferenceReglementInterieur { get; set; }

        /// <summary>Nom du signataire des lettres.</summary>
        public string Signataire { get; set; }

        /// <summary>Qualité du signataire (Directeur général, DRH…).</summary>
        public string FonctionSignataire { get; set; }

        public static ParametresDiscipline Defaut()
        {
            return new ParametresDiscipline();
        }

        public ParametresDiscipline Clone()
        {
            return (ParametresDiscipline)MemberwiseClone();
        }
    }

    /// <summary>Une pièce du dossier disciplinaire, copiée dans le dossier de l'entreprise.</summary>
    public class PieceDisciplinaire
    {
        public int Id { get; set; }
        public int ProcedureId { get; set; }
        public TypePiece Type { get; set; }
        public string Libelle { get; set; }

        /// <summary>Nom du fichier dans le répertoire des pièces de la procédure.</summary>
        public string NomFichier { get; set; }

        /// <summary>Nom du fichier tel qu'il a été joint (pour l'affichage).</summary>
        public string NomOriginal { get; set; }

        public DateTime DateAjout { get; set; } = DateTime.Now;

        /// <summary>Lettre produite par le logiciel (et non pièce jointe par l'utilisateur).</summary>
        public bool EstGeneree { get; set; }

        public string TypeLibelle { get { return LibellesDiscipline.Piece(Type); } }
        public string DateAjoutLibelle { get { return DateAjout.ToString("dd/MM/yyyy HH:mm", LibellesDiscipline.Fr); } }
    }

    /// <summary>Une pièce que la procédure exige à son stade, et si elle figure au dossier.</summary>
    public class PieceExigee
    {
        public TypePiece Type { get; set; }
        public string Motif { get; set; }
        public bool Fournie { get; set; }

        public string Libelle { get { return LibellesDiscipline.Piece(Type); } }
        public string Etat { get { return Fournie ? "Au dossier" : "Manquante"; } }
    }

    /// <summary>Résultat d'un contrôle : une phrase, un niveau.</summary>
    public class AlerteDiscipline
    {
        public AlerteDiscipline(NiveauAlerte niveau, string message)
        {
            Niveau = niveau;
            Message = message;
        }

        public NiveauAlerte Niveau { get; }
        public string Message { get; }

        public string NiveauLibelle { get { return LibellesDiscipline.Niveau(Niveau); } }

        /// <summary>Couleur de la pastille (« #C62828 »).</summary>
        public string Couleur
        {
            get
            {
                switch (Niveau)
                {
                    case NiveauAlerte.Bloquant: return "#C62828";
                    case NiveauAlerte.Avertissement: return "#E65100";
                    default: return "#14675A";
                }
            }
        }

        public override string ToString()
        {
            return NiveauLibelle + " : " + Message;
        }
    }

    /// <summary>Une échéance de procédure, pour l'échéancier transverse.</summary>
    public class EcheanceDiscipline
    {
        public int ProcedureId { get; set; }
        public string Reference { get; set; }
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public DateTime Date { get; set; }
        public string Libelle { get; set; }

        /// <summary>Jours restants à la date de référence (négatif : échéance passée).</summary>
        public int JoursRestants { get; set; }

        public bool EstDepassee { get { return JoursRestants < 0; } }

        public string DateLibelle { get { return Date.ToString("ddd dd/MM/yyyy", LibellesDiscipline.Fr); } }

        public string DelaiLibelle
        {
            get
            {
                if (JoursRestants == 0) return "Aujourd'hui";
                if (JoursRestants == 1) return "Demain";
                if (JoursRestants > 0) return string.Format("Dans {0} j", JoursRestants);
                return string.Format("Dépassée de {0} j", -JoursRestants);
            }
        }
    }

    /// <summary>Une ligne du registre des sanctions notifiées.</summary>
    public class LigneRegistreSanction
    {
        public int ProcedureId { get; set; }
        public string Reference { get; set; }
        public string EmployeeId { get; set; }
        public string NomSalarie { get; set; }
        public DateTime DateFaits { get; set; }
        public string CategorieFaits { get; set; }
        public string Sanction { get; set; }
        public NatureSanction Nature { get; set; }
        public DateTime DateNotification { get; set; }
        public decimal JoursMiseAPied { get; set; }
        public DateTime? DateEffacement { get; set; }
        public bool EstEffacee { get; set; }
        public bool Recours { get; set; }

        public string DateFaitsLibelle { get { return LibellesDiscipline.Date(DateFaits); } }
        public string DateNotificationLibelle { get { return LibellesDiscipline.Date(DateNotification); } }
        public string EffacementLibelle
        {
            get
            {
                if (!DateEffacement.HasValue) return "Jamais";
                return (EstEffacee ? "Effacée le " : "Le ") + LibellesDiscipline.Date(DateEffacement);
            }
        }
    }

    /// <summary>
    /// Une procédure disciplinaire : des faits reprochés à un salarié jusqu'à la décision
    /// notifiée (ou au classement sans suite). L'étape atteinte n'est pas saisie : elle se
    /// déduit des dates renseignées, ce qui interdit les états incohérents.
    /// </summary>
    public class ProcedureDisciplinaire : INotifyPropertyChanged
    {
        private int _id;
        private string _reference;
        private string _employeeId;
        private string _nomSalarie;
        private string _fonction;
        private DateTime _dateFaits = DateTime.Today;
        private DateTime _dateConnaissance = DateTime.Today;
        private string _lieuFaits;
        private string _categorieFaits;
        private string _descriptionFaits;
        private string _temoins;
        private GraviteFaute _gravite = GraviteFaute.Legere;
        private bool _salarieProtege;
        private string _mandatProtege;

        private DateTime? _dateDemandeExplication;
        private DateTime? _dateReponse;
        private string _resumeReponse;

        private DateTime? _conservatoireDebut;
        private DateTime? _conservatoireFin;

        private DateTime? _dateConvocation;
        private DateTime? _dateEntretien;
        private string _heureLieuEntretien;
        private string _assistantEntretien;
        private string _compteRenduEntretien;

        private DecisionInspection _decisionInspection;
        private DateTime? _dateDemandeInspection;
        private DateTime? _dateDecisionInspection;
        private string _referenceInspection;

        private int? _typeSanctionId;
        private DateTime? _dateDecision;
        private string _motifsDecision;
        private DateTime? _miseAPiedDebut;
        private DateTime? _miseAPiedFin;
        private decimal _miseAPiedJours;
        private DateTime? _dateEffetLicenciement;
        private bool _classeeSansSuite;
        private string _motifClassement;

        private DateTime? _dateNotification;
        private string _modeNotification;
        private DateTime? _dateAccuseReception;

        private DateTime? _dateRecours;
        private string _instanceRecours;
        private string _issueRecours;

        private int? _absenceMiseAPiedId;
        private int? _absenceConservatoireId;
        private string _commentaire;
        private DateTime _dateCreation = DateTime.Now;
        private DateTime _dateModification = DateTime.Now;
        private string _creePar;

        // ── Identification ───────────────────────────────────────────────

        public int Id { get { return _id; } set { Definir(ref _id, value); } }

        /// <summary>Référence attribuée à la création : DISC-2026-0007.</summary>
        public string Reference { get { return _reference; } set { Definir(ref _reference, value); } }

        public string EmployeeId { get { return _employeeId; } set { Definir(ref _employeeId, value); } }
        public string NomSalarie { get { return _nomSalarie; } set { Definir(ref _nomSalarie, value); } }
        public string Fonction { get { return _fonction; } set { Definir(ref _fonction, value); } }

        // ── Faits ────────────────────────────────────────────────────────

        public DateTime DateFaits { get { return _dateFaits; } set { Definir(ref _dateFaits, value.Date); } }

        /// <summary>Date à laquelle l'employeur a eu connaissance des faits : point de départ du délai d'engagement.</summary>
        public DateTime DateConnaissance { get { return _dateConnaissance; } set { Definir(ref _dateConnaissance, value.Date); } }

        public string LieuFaits { get { return _lieuFaits; } set { Definir(ref _lieuFaits, value); } }
        public string CategorieFaits { get { return _categorieFaits; } set { Definir(ref _categorieFaits, value); } }
        public string DescriptionFaits { get { return _descriptionFaits; } set { Definir(ref _descriptionFaits, value); } }
        public string Temoins { get { return _temoins; } set { Definir(ref _temoins, value); } }

        public GraviteFaute Gravite
        {
            get { return _gravite; }
            set { if (Definir(ref _gravite, value)) OnPropertyChanged(nameof(GraviteLibelle)); }
        }

        /// <summary>Délégué du personnel, délégué syndical… : licenciement soumis à l'autorisation de l'inspecteur du travail.</summary>
        public bool SalarieProtege { get { return _salarieProtege; } set { Definir(ref _salarieProtege, value); } }
        public string MandatProtege { get { return _mandatProtege; } set { Definir(ref _mandatProtege, value); } }

        // ── Demande d'explication ────────────────────────────────────────

        public DateTime? DateDemandeExplication { get { return _dateDemandeExplication; } set { DefinirDate(ref _dateDemandeExplication, value); } }
        public DateTime? DateReponse { get { return _dateReponse; } set { DefinirDate(ref _dateReponse, value); } }
        public string ResumeReponse { get { return _resumeReponse; } set { Definir(ref _resumeReponse, value); } }

        // ── Mise à pied conservatoire ────────────────────────────────────

        /// <summary>Mesure d'attente (pas une sanction) : salaire maintenu, sauf licenciement pour faute lourde.</summary>
        public DateTime? ConservatoireDebut { get { return _conservatoireDebut; } set { DefinirDate(ref _conservatoireDebut, value); } }
        public DateTime? ConservatoireFin { get { return _conservatoireFin; } set { DefinirDate(ref _conservatoireFin, value); } }

        // ── Entretien préalable ──────────────────────────────────────────

        public DateTime? DateConvocation { get { return _dateConvocation; } set { DefinirDate(ref _dateConvocation, value); } }
        public DateTime? DateEntretien { get { return _dateEntretien; } set { DefinirDate(ref _dateEntretien, value); } }

        /// <summary>Heure et lieu de l'entretien, repris dans la convocation (« 9 h 00, bureau de la DRH »).</summary>
        public string HeureLieuEntretien { get { return _heureLieuEntretien; } set { Definir(ref _heureLieuEntretien, value); } }

        /// <summary>Personne qui a assisté le salarié (délégué du personnel, collègue).</summary>
        public string AssistantEntretien { get { return _assistantEntretien; } set { Definir(ref _assistantEntretien, value); } }
        public string CompteRenduEntretien { get { return _compteRenduEntretien; } set { Definir(ref _compteRenduEntretien, value); } }

        // ── Inspection du travail (salarié protégé) ──────────────────────

        public DecisionInspection DecisionInspection
        {
            get { return _decisionInspection; }
            set { if (Definir(ref _decisionInspection, value)) OnPropertyChanged(nameof(InspectionLibelle)); }
        }
        public DateTime? DateDemandeInspection { get { return _dateDemandeInspection; } set { DefinirDate(ref _dateDemandeInspection, value); } }
        public DateTime? DateDecisionInspection { get { return _dateDecisionInspection; } set { DefinirDate(ref _dateDecisionInspection, value); } }
        public string ReferenceInspection { get { return _referenceInspection; } set { Definir(ref _referenceInspection, value); } }

        // ── Décision ─────────────────────────────────────────────────────

        /// <summary>Sanction envisagée, puis retenue une fois la date de décision renseignée.</summary>
        public int? TypeSanctionId { get { return _typeSanctionId; } set { Definir(ref _typeSanctionId, value); } }

        public DateTime? DateDecision { get { return _dateDecision; } set { DefinirDate(ref _dateDecision, value); } }

        /// <summary>Motifs précis et vérifiables : obligatoires dans la lettre de licenciement.</summary>
        public string MotifsDecision { get { return _motifsDecision; } set { Definir(ref _motifsDecision, value); } }

        public DateTime? MiseAPiedDebut { get { return _miseAPiedDebut; } set { DefinirDate(ref _miseAPiedDebut, value); } }
        public DateTime? MiseAPiedFin { get { return _miseAPiedFin; } set { DefinirDate(ref _miseAPiedFin, value); } }

        /// <summary>Jours de mise à pied retenus sur le salaire (proposés d'après les dates, dimanches exclus).</summary>
        public decimal MiseAPiedJours { get { return _miseAPiedJours; } set { Definir(ref _miseAPiedJours, value); } }

        /// <summary>Date de fin du contrat (licenciement).</summary>
        public DateTime? DateEffetLicenciement { get { return _dateEffetLicenciement; } set { DefinirDate(ref _dateEffetLicenciement, value); } }

        public bool ClasseeSansSuite { get { return _classeeSansSuite; } set { Definir(ref _classeeSansSuite, value); } }
        public string MotifClassement { get { return _motifClassement; } set { Definir(ref _motifClassement, value); } }

        // ── Notification ─────────────────────────────────────────────────

        public DateTime? DateNotification { get { return _dateNotification; } set { DefinirDate(ref _dateNotification, value); } }
        public string ModeNotification { get { return _modeNotification; } set { Definir(ref _modeNotification, value); } }
        public DateTime? DateAccuseReception { get { return _dateAccuseReception; } set { DefinirDate(ref _dateAccuseReception, value); } }

        // ── Recours ──────────────────────────────────────────────────────

        public DateTime? DateRecours { get { return _dateRecours; } set { DefinirDate(ref _dateRecours, value); } }
        public string InstanceRecours { get { return _instanceRecours; } set { Definir(ref _instanceRecours, value); } }
        public string IssueRecours { get { return _issueRecours; } set { Definir(ref _issueRecours, value); } }

        // ── Liens et suivi ───────────────────────────────────────────────

        /// <summary>Absence « Mise à pied » créée dans PAI-04 pour la retenue sur salaire.</summary>
        public int? AbsenceMiseAPiedId { get { return _absenceMiseAPiedId; } set { Definir(ref _absenceMiseAPiedId, value); } }

        /// <summary>Absence créée pour la mise à pied conservatoire non payée (licenciement pour faute lourde).</summary>
        public int? AbsenceConservatoireId { get { return _absenceConservatoireId; } set { Definir(ref _absenceConservatoireId, value); } }

        public string Commentaire { get { return _commentaire; } set { Definir(ref _commentaire, value); } }
        public DateTime DateCreation { get { return _dateCreation; } set { Definir(ref _dateCreation, value); } }
        public DateTime DateModification { get { return _dateModification; } set { Definir(ref _dateModification, value); } }
        public string CreePar { get { return _creePar; } set { Definir(ref _creePar, value); } }

        /// <summary>Sanction du catalogue correspondant à <see cref="TypeSanctionId"/> (non persisté).</summary>
        public TypeSanction TypeSanction { get; set; }

        // ── Calculés ─────────────────────────────────────────────────────

        public StatutProcedure Statut
        {
            get
            {
                if (_classeeSansSuite) return StatutProcedure.ClasseeSansSuite;
                if (_dateNotification.HasValue && _dateDecision.HasValue) return StatutProcedure.Notifiee;
                if (_dateDecision.HasValue) return StatutProcedure.DecisionPrise;
                if (_dateConvocation.HasValue) return StatutProcedure.EntretienPrevu;
                if (_dateDemandeExplication.HasValue) return StatutProcedure.ExplicationDemandee;
                return StatutProcedure.Ouverte;
            }
        }

        public string StatutLibelle { get { return LibellesDiscipline.Statut(Statut); } }

        /// <summary>Vraie quand plus rien n'est attendu de l'employeur (notifiée ou classée).</summary>
        public bool EstClose
        {
            get { return Statut == StatutProcedure.Notifiee || Statut == StatutProcedure.ClasseeSansSuite; }
        }

        /// <summary>Premier acte de procédure : il interrompt le délai d'engagement.</summary>
        public DateTime? DateEngagement
        {
            get
            {
                DateTime? d = null;
                foreach (var x in new[] { _dateDemandeExplication, _dateConvocation, _conservatoireDebut })
                    if (x.HasValue && (!d.HasValue || x.Value < d.Value)) d = x;
                return d;
            }
        }

        public string GraviteLibelle { get { return LibellesDiscipline.Gravite(_gravite); } }
        public string InspectionLibelle { get { return LibellesDiscipline.Inspection(_decisionInspection); } }

        public string SanctionLibelle
        {
            get
            {
                if (_classeeSansSuite) return "Sans suite";
                if (TypeSanction == null) return "—";
                string lib = TypeSanction.Libelle;
                if (TypeSanction.EstMiseAPied && _miseAPiedJours > 0m)
                    lib += string.Format(LibellesDiscipline.Fr, " ({0:0.##} j)", _miseAPiedJours);
                return _dateDecision.HasValue ? lib : lib + " (envisagée)";
            }
        }

        public string DateFaitsLibelle { get { return LibellesDiscipline.Date(_dateFaits); } }

        public string Designation
        {
            get { return string.Format("{0} — {1}", _reference ?? "(nouvelle)", _nomSalarie ?? _employeeId); }
        }

        public bool EstNouvelle { get { return _id <= 0; } }

        /// <summary>Copie détachée pour l'édition (l'écran n'altère pas la liste avant enregistrement).</summary>
        public ProcedureDisciplinaire Clone()
        {
            var c = (ProcedureDisciplinaire)MemberwiseClone();
            c.PropertyChanged = null;
            return c;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private static readonly string[] DependantsDuStatut =
        {
            nameof(Statut), nameof(StatutLibelle), nameof(EstClose), nameof(SanctionLibelle), nameof(DateEngagement)
        };

        private bool DefinirDate(ref DateTime? champ, DateTime? valeur, [CallerMemberName] string nom = null)
        {
            return Definir(ref champ, valeur.HasValue ? valeur.Value.Date : (DateTime?)null, nom);
        }

        private bool Definir<T>(ref T champ, T valeur, [CallerMemberName] string nom = null)
        {
            if (EqualityComparer<T>.Default.Equals(champ, valeur)) return false;
            champ = valeur;
            OnPropertyChanged(nom);
            foreach (var d in DependantsDuStatut) OnPropertyChanged(d);
            if (nom == nameof(Reference) || nom == nameof(NomSalarie) || nom == nameof(EmployeeId))
                OnPropertyChanged(nameof(Designation));
            if (nom == nameof(DateFaits)) OnPropertyChanged(nameof(DateFaitsLibelle));
            return true;
        }

        public void Notifier(string nom)
        {
            OnPropertyChanged(nom);
        }

        private void OnPropertyChanged(string nom)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(nom));
        }
    }
}

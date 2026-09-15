using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IUTSCalculator.Services;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Type d'avantage en nature selon la typologie fiscale et sociale burkinabè.
    /// </summary>
    public enum TypeAvantageNature
    {
        /// <summary>Logement de fonction (Art. 106 CGI : forfait 20% du salaire de base plafonné à 75 000 FCFA ou valeur locative réelle).</summary>
        Logement = 0,

        /// <summary>Véhicule de fonction ou de service avec usage mixte/personnel.</summary>
        Vehicule = 1,

        /// <summary>Prise en charge de l'électricité (factures SONABEL ou forfait mensuel).</summary>
        Electricite = 2,

        /// <summary>Prise en charge de l'eau (factures ONEA ou forfait mensuel).</summary>
        Eau = 3,

        /// <summary>Gens de maison et domesticité (gardiennage, cuisinier, chauffeur, jardinier).</summary>
        Domesticite = 4,

        /// <summary>Nourriture ou cantine d'entreprise.</summary>
        Nourriture = 5,

        /// <summary>Autres prestations fournies en nature (téléphone/internet personnel, etc.).</summary>
        Autre = 6
    }

    /// <summary>
    /// Mode d'évaluation de la valeur de l'avantage en nature.
    /// </summary>
    public enum ModeEvaluationAvantage
    {
        /// <summary>Évaluation forfaitaire réglementaire (ex: barème Art. 106 CGI pour le logement).</summary>
        ForfaitaireLegal = 0,

        /// <summary>Prise en charge à la valeur réelle des factures, loyers ou dépenses engagées par l'employeur.</summary>
        ValeurReelle = 1
    }

    /// <summary>
    /// Entité représentant un avantage en nature accordé à un employé (module PAI-08).
    /// </summary>
    public class AvantageNature : INotifyPropertyChanged
    {
        private int _id;
        private string _employeeId;
        private string _nomSalarie;
        private TypeAvantageNature _typeAvantage;
        private string _libelle;
        private ModeEvaluationAvantage _modeEvaluation;
        private decimal _montantAvantage;
        private decimal _partSalariale;
        private DateTime _dateDebut;
        private DateTime? _dateFin;
        private bool _actif = true;
        private bool _soumisIUTS = true;
        private bool _soumisCNSS = true;
        private bool _soumisTPA = true;
        private string _codeRubriqueGain = "AVNAT_LOG";
        private string _codeRubriqueRetenue = "RET_AVNAT";
        private string _commentaire;
        private DateTime _dateCreation;
        private DateTime _dateModification;

        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string EmployeeId
        {
            get => _employeeId;
            set => SetProperty(ref _employeeId, value);
        }

        public string NomSalarie
        {
            get => _nomSalarie;
            set => SetProperty(ref _nomSalarie, value);
        }

        public TypeAvantageNature TypeAvantage
        {
            get => _typeAvantage;
            set
            {
                if (SetProperty(ref _typeAvantage, value))
                {
                    OnPropertyChanged(nameof(TypeLibelle));
                    // Met à jour le code rubrique par défaut si pas personnalisé
                    MettreAJourCodeRubriqueParDefaut();
                }
            }
        }

        public string Libelle
        {
            get => _libelle;
            set => SetProperty(ref _libelle, value);
        }

        public ModeEvaluationAvantage ModeEvaluation
        {
            get => _modeEvaluation;
            set
            {
                if (SetProperty(ref _modeEvaluation, value))
                    OnPropertyChanged(nameof(ModeLibelle));
            }
        }

        public decimal MontantAvantage
        {
            get => _montantAvantage;
            set
            {
                if (SetProperty(ref _montantAvantage, value))
                    OnPropertyChanged(nameof(ValeurNetteImposable));
            }
        }

        public decimal PartSalariale
        {
            get => _partSalariale;
            set
            {
                if (SetProperty(ref _partSalariale, value))
                    OnPropertyChanged(nameof(ValeurNetteImposable));
            }
        }

        /// <summary>
        /// Valeur nette imposable et cotisable réintégrée dans l'assiette brute :
        /// Montant brut de l'avantage diminué de la retenue/participation déjà payée par le travailleur.
        /// </summary>
        public decimal ValeurNetteImposable => Math.Max(0m, MontantAvantage - PartSalariale);

        public DateTime DateDebut
        {
            get => _dateDebut;
            set => SetProperty(ref _dateDebut, value);
        }

        public DateTime? DateFin
        {
            get => _dateFin;
            set
            {
                if (SetProperty(ref _dateFin, value))
                    OnPropertyChanged(nameof(StatutLibelle));
            }
        }

        public bool Actif
        {
            get => _actif;
            set
            {
                if (SetProperty(ref _actif, value))
                    OnPropertyChanged(nameof(StatutLibelle));
            }
        }

        public bool SoumisIUTS
        {
            get => _soumisIUTS;
            set => SetProperty(ref _soumisIUTS, value);
        }

        public bool SoumisCNSS
        {
            get => _soumisCNSS;
            set => SetProperty(ref _soumisCNSS, value);
        }

        public bool SoumisTPA
        {
            get => _soumisTPA;
            set => SetProperty(ref _soumisTPA, value);
        }

        public string CodeRubriqueGain
        {
            get => _codeRubriqueGain;
            set => SetProperty(ref _codeRubriqueGain, value);
        }

        public string CodeRubriqueRetenue
        {
            get => _codeRubriqueRetenue;
            set => SetProperty(ref _codeRubriqueRetenue, value);
        }

        public string Commentaire
        {
            get => _commentaire;
            set => SetProperty(ref _commentaire, value);
        }

        public DateTime DateCreation
        {
            get => _dateCreation;
            set => SetProperty(ref _dateCreation, value);
        }

        public DateTime DateModification
        {
            get => _dateModification;
            set => SetProperty(ref _dateModification, value);
        }

        #region Propriétés dérivées d'affichage

        public string TypeLibelle
        {
            get
            {
                switch (TypeAvantage)
                {
                    case TypeAvantageNature.Logement: return "Logement de fonction";
                    case TypeAvantageNature.Vehicule: return "Véhicule de fonction";
                    case TypeAvantageNature.Electricite: return "Électricité (SONABEL)";
                    case TypeAvantageNature.Eau: return "Eau (ONEA)";
                    case TypeAvantageNature.Domesticite: return "Domesticité / Gens de maison";
                    case TypeAvantageNature.Nourriture: return "Nourriture / Cantine";
                    default: return "Autre prestation en nature";
                }
            }
        }

        public string ModeLibelle
        {
            get
            {
                switch (ModeEvaluation)
                {
                    case ModeEvaluationAvantage.ForfaitaireLegal: return "Forfait réglementaire (CGI)";
                    case ModeEvaluationAvantage.ValeurReelle: return "Valeur réelle facturée";
                    default: return "Indéterminé";
                }
            }
        }

        public string StatutLibelle
        {
            get
            {
                if (!Actif) return "Inactif / Résilié";
                if (DateFin.HasValue && DateFin.Value.Date < DateTime.Today) return "Expiré";
                return "En cours";
            }
        }

        #endregion

        public bool EstEnCours(int annee, int mois)
        {
            if (!Actif) return false;

            var debutMois = new DateTime(annee, mois, 1);
            var finMois = new DateTime(annee, mois, DateTime.DaysInMonth(annee, mois));

            if (DateDebut.Date > finMois) return false;
            if (DateFin.HasValue && DateFin.Value.Date < debutMois) return false;

            return true;
        }

        public void MettreAJourCodeRubriqueParDefaut()
        {
            switch (TypeAvantage)
            {
                case TypeAvantageNature.Logement:
                    CodeRubriqueGain = "AVNAT_LOG";
                    if (string.IsNullOrWhiteSpace(Libelle)) Libelle = "Avantage en nature logement";
                    break;
                case TypeAvantageNature.Vehicule:
                    CodeRubriqueGain = "AVNAT_VEH";
                    if (string.IsNullOrWhiteSpace(Libelle)) Libelle = "Avantage en nature véhicule";
                    break;
                case TypeAvantageNature.Electricite:
                case TypeAvantageNature.Eau:
                    CodeRubriqueGain = "AVNAT_ENG";
                    if (string.IsNullOrWhiteSpace(Libelle))
                        Libelle = TypeAvantage == TypeAvantageNature.Electricite ? "Prise en charge électricité" : "Prise en charge eau";
                    break;
                case TypeAvantageNature.Domesticite:
                    CodeRubriqueGain = "AVNAT_DOM";
                    if (string.IsNullOrWhiteSpace(Libelle)) Libelle = "Avantage en nature domesticité";
                    break;
                default:
                    CodeRubriqueGain = "AVNAT_DIV";
                    if (string.IsNullOrWhiteSpace(Libelle)) Libelle = "Avantage en nature divers";
                    break;
            }

            CodeRubriqueRetenue = "RET_AVNAT";
        }

        public AvantageNature Cloner()
        {
            return new AvantageNature
            {
                Id = this.Id,
                EmployeeId = this.EmployeeId,
                NomSalarie = this.NomSalarie,
                TypeAvantage = this.TypeAvantage,
                Libelle = this.Libelle,
                ModeEvaluation = this.ModeEvaluation,
                MontantAvantage = this.MontantAvantage,
                PartSalariale = this.PartSalariale,
                DateDebut = this.DateDebut,
                DateFin = this.DateFin,
                Actif = this.Actif,
                SoumisIUTS = this.SoumisIUTS,
                SoumisCNSS = this.SoumisCNSS,
                SoumisTPA = this.SoumisTPA,
                CodeRubriqueGain = this.CodeRubriqueGain,
                CodeRubriqueRetenue = this.CodeRubriqueRetenue,
                Commentaire = this.Commentaire,
                DateCreation = this.DateCreation,
                DateModification = this.DateModification
            };
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }

    /// <summary>
    /// Ce que les avantages en nature d'un salarié apportent au bulletin du mois (module PAI-08).
    /// Réintègre l'avantage dans le brut imposable/cotisable et ajoute la reprise sur le net à payer.
    /// </summary>
    public sealed class ApportAvantagesNature
    {
        private readonly List<LigneApport> _lignes = new List<LigneApport>();
        private readonly List<int> _avantageIds = new List<int>();

        public IReadOnlyList<LigneApport> Lignes => _lignes;
        public IReadOnlyList<int> AvantageIds => _avantageIds;

        public decimal GainsImposablesEtCotisables { get; private set; }
        public decimal GainsImposablesSeulement { get; private set; }
        public decimal GainsCotisablesSeulement { get; private set; }
        public decimal GainsNonSoumis { get; private set; }
        public decimal GainsExoneresTPA { get; private set; }
        public decimal Retenues { get; private set; }

        public bool EstVide => _lignes.Count == 0 &&
                               GainsImposablesEtCotisables == 0m &&
                               GainsImposablesSeulement == 0m &&
                               GainsCotisablesSeulement == 0m &&
                               GainsNonSoumis == 0m &&
                               Retenues == 0m;

        public ApportAvantagesNature()
        {
        }

        public void AjouterAvantage(
            string codeRubriqueGain,
            string libelleGain,
            string codeRubriqueRetenue,
            decimal valeurNetteImposable,
            bool soumisIUTS,
            bool soumisCNSS,
            bool soumisTPA,
            int? avantageId = null)
        {
            if (valeurNetteImposable <= 0m) return;

            if (avantageId.HasValue && !_avantageIds.Contains(avantageId.Value))
                _avantageIds.Add(avantageId.Value);

            // 1. Réintégration dans le brut imposable / cotisable (Sens Gain)
            if (soumisIUTS && soumisCNSS)
                GainsImposablesEtCotisables += valeurNetteImposable;
            else if (soumisIUTS)
                GainsImposablesSeulement += valeurNetteImposable;
            else if (soumisCNSS)
                GainsCotisablesSeulement += valeurNetteImposable;
            else
                GainsNonSoumis += valeurNetteImposable;

            if (!soumisTPA)
                GainsExoneresTPA += valeurNetteImposable;

            _lignes.Add(new LigneApport(
                string.IsNullOrWhiteSpace(codeRubriqueGain) ? "AVNAT_LOG" : codeRubriqueGain,
                libelleGain ?? "Avantage en nature",
                SensRubrique.Gain,
                0m,
                valeurNetteImposable,
                soumisIUTS,
                soumisCNSS,
                soumisTPA));

            // 2. Déduction en bas de bulletin du net à payer (Sens Retenue)
            // L'avantage étant déjà perçu en nature, on reprend la valeur nette pour ne pas verser d'espèces indues
            Retenues += valeurNetteImposable;

            _lignes.Add(new LigneApport(
                string.IsNullOrWhiteSpace(codeRubriqueRetenue) ? "RET_AVNAT" : codeRubriqueRetenue,
                "Reprise avantage en nature (" + (libelleGain ?? "nature") + ")",
                SensRubrique.Retenue,
                0m,
                valeurNetteImposable,
                false,
                false,
                false));
        }

        public static ApportAvantagesNature Aucun() => new ApportAvantagesNature();
    }
}

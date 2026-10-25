using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente un employé avec ses informations de base et fiscales
    /// </summary>
    public class Employee : INotifyPropertyChanged
    {
        private string _firstName;
        private string _lastName;
        private string _employeeId;
        private decimal _baseSalary;
        private decimal _housingAllowance;
        private decimal _transportAllowance;
        private decimal _functionAllowance;
        private decimal _sujetionAllowance;
        private int _familyCharges;
        private bool _isSpouseWorking;
        private decimal _pensionContribution;
        private string _category;
        private string _service;
        private bool _isHigherCategory;
        private string _situationMatrimoniale;
        private string _numeroCnss;
        private DateTime? _dateNaissance;
        private string _lieuNaissance;
        private string _sexe;
        private string _nationalite;
        private string _numeroCnib;
        private DateTime? _dateDelivranceCnib;
        private string _lieuDelivranceCnib;
        private string _adresse;
        private string _telephone;
        private string _email;
        private string _numeroIfu;
        private string _banque;
        private string _numeroCompte;
        private string _modeReglement;
        private int? _conventionId;
        private string _echelon;
        private DateTime? _dateSortie;
        private string _motifDepart;
        private int _statutSalarie;
        private int? _posteId;
        private string _managerMatricule;
        private decimal _soldeCongesReprise;
        private DateTime? _dateRepriseConges;
        private string _posteLibelle;
        private string _managerNom;

        /// <summary>
        /// Prénom de l'employé
        /// </summary>
        public string FirstName
        {
            get => _firstName;
            set
            {
                if (_firstName != value)
                {
                    _firstName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FullName));
                    OnPropertyChanged(nameof(FullNameWithId));
                }
            }
        }

        /// <summary>
        /// Nom de l'employé
        /// </summary>
        public string LastName
        {
            get => _lastName;
            set
            {
                if (_lastName != value)
                {
                    _lastName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FullName));
                    OnPropertyChanged(nameof(FullNameWithId));
                }
            }
        }

        /// <summary>
        /// Identificateur unique de l'employé
        /// </summary>
        public string EmployeeId
        {
            get => _employeeId;
            set
            {
                if (_employeeId != value)
                {
                    _employeeId = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Matricule));
                    OnPropertyChanged(nameof(FullNameWithId));
                }
            }
        }

        /// <summary>
        /// Matricule du salarié (alias de EmployeeId pour compatibilité métier RH/Paie et liaisons XAML).
        /// </summary>
        public string Matricule
        {
            get => _employeeId;
            set => EmployeeId = value;
        }

        /// <summary>
        /// Salaire de base de l'employé
        /// </summary>
        public decimal BaseSalary
        {
            get => _baseSalary;
            set
            {
                if (_baseSalary != value)
                {
                    _baseSalary = value;
                    OnPropertyChanged();
                    // Recalculer les plafonds d'indemnités
                    OnPropertyChanged(nameof(MaxHousingAllowance));
                    OnPropertyChanged(nameof(MaxFunctionAllowance));
                    OnPropertyChanged(nameof(MaxTransportAllowance));
                }
            }
        }

        /// <summary>
        /// Indemnité de logement
        /// </summary>
        public decimal HousingAllowance
        {
            get => _housingAllowance;
            set
            {
                if (_housingAllowance != value)
                {
                    _housingAllowance = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indemnité de transport
        /// </summary>
        public decimal TransportAllowance
        {
            get => _transportAllowance;
            set
            {
                if (_transportAllowance != value)
                {
                    _transportAllowance = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indemnité de fonction
        /// </summary>
        public decimal FunctionAllowance
        {
            get => _functionAllowance;
            set
            {
                if (_functionAllowance != value)
                {
                    _functionAllowance = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Fonction de l'employé (poste ou titre)
        /// </summary>
        public string Fonction { get; set; }

        /// <summary>
        /// Date d'entrée de l'employé dans l'entreprise
        /// </summary>
        public DateTime DateEntree { get; set; }

        /// <summary>
        /// Date d'entrée sous forme nullable, pour la liaison avec un DatePicker : une date
        /// non renseignée doit laisser le contrôle vide plutôt qu'afficher 01/01/0001.
        /// C'est la même convention que celle déjà retenue par le service des bulletins.
        /// </summary>
        public DateTime? DateEntreeNullable
        {
            get { return DateEntree == default(DateTime) ? (DateTime?)null : DateEntree; }
            set { DateEntree = value ?? default(DateTime); }
        }


        public decimal TotalIndemnites
        {
            get
            {
                return
                    (HousingAllowance > 0 ? HousingAllowance : 0) +
                    (FunctionAllowance > 0 ? FunctionAllowance : 0) +
                    (TransportAllowance > 0 ? TransportAllowance : 0) +
                    (SujetionAllowance > 0 ? SujetionAllowance : 0);
            }
        }


        /// <summary>
        /// Indemnité de fonction
        /// </summary>
        public decimal SujetionAllowance
        {
            get => _sujetionAllowance;
            set
            {
                if (_sujetionAllowance != value)
                {
                    _sujetionAllowance = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Nombre de charges familiales
        /// </summary>
        public int FamilyCharges
        {
            get => _familyCharges;
            set
            {
                if (_familyCharges != value)
                {
                    _familyCharges = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indique si le conjoint travaille
        /// </summary>
        public bool IsSpouseWorking
        {
            get => _isSpouseWorking;
            set
            {
                if (_isSpouseWorking != value)
                {
                    _isSpouseWorking = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Cotisation de pension/retraite
        /// </summary>
        public decimal PensionContribution
        {
            get => _pensionContribution;
            set
            {
                if (_pensionContribution != value)
                {
                    _pensionContribution = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Catégorie du salarié (P, A, B, etc.)
        /// </summary>
        public string Category
        {
            get => _category;
            set
            {
                if (_category != value)
                {
                    _category = value;
                    DetermineIfHigherCategory();
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Service ou département du salarié (ex. « Comptabilité », « Production »),
        /// utilisé comme axe de ventilation du tableau de bord masse salariale (module
        /// PIL-01) en complément de la catégorie. Purement déclaratif : rien dans le
        /// moteur de paie n'en dépend.
        /// </summary>
        public string Service
        {
            get => _service;
            set { if (_service != value) { _service = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Identifiant de la convention collective applicable (module SOC-05).
        /// </summary>
        public int? ConventionId
        {
            get => _conventionId;
            set
            {
                if (_conventionId != value)
                {
                    _conventionId = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Échelon conventionnel dans la grille de salaire (module SOC-05, ex. "A", "B", "C").
        /// </summary>
        public string Echelon
        {
            get => _echelon;
            set
            {
                if (_echelon != value)
                {
                    _echelon = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Date effective de sortie de l'entreprise (fin de contrat, démission, etc.).</summary>
        public DateTime? DateSortie
        {
            get => _dateSortie;
            set
            {
                if (_dateSortie != value)
                {
                    _dateSortie = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EstActif));
                }
            }
        }

        /// <summary>Motif de rupture ou de départ (Démission, Licenciement, Retraite, etc.).</summary>
        public string MotifDepart
        {
            get => _motifDepart;
            set
            {
                if (_motifDepart != value)
                {
                    _motifDepart = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Statut de présence du salarié : 0 = Actif, 1 = Sorti / Inactif, 2 = Suspendu.</summary>
        public int StatutSalarie
        {
            get => _statutSalarie;
            set
            {
                if (_statutSalarie != value)
                {
                    _statutSalarie = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EstActif));
                    OnPropertyChanged(nameof(StatutLibelle));
                }
            }
        }

        /// <summary>Indique si le salarié fait toujours partie des effectifs actifs de l'entreprise.</summary>
        public bool EstActif => StatutSalarie == 0 && (!DateSortie.HasValue || DateSortie.Value.Date >= DateTime.Today);

        /// <summary>Libellé textuel du statut pour l'IHM et les rapports.</summary>
        public string StatutLibelle
        {
            get
            {
                switch (StatutSalarie)
                {
                    case 1: return "Sorti";
                    case 2: return "Suspendu";
                    default: return "Actif";
                }
            }
        }

        /// <summary>
        /// Calcule l'ancienneté en années révolues à une date de référence (ex. fin de mois de paie).
        /// </summary>
        public int ObtenirAncienneteAnnees(DateTime aDate)
        {
            if (DateEntree == default(DateTime) || DateEntree > aDate)
                return 0;

            int annees = aDate.Year - DateEntree.Year;
            if (aDate.Month < DateEntree.Month || (aDate.Month == DateEntree.Month && aDate.Day < DateEntree.Day))
                annees--;

            return Math.Max(0, annees);
        }

        /// <summary>
        /// Calcule le reliquat de mois d'ancienneté après les années révolues.
        /// </summary>
        public int ObtenirAncienneteMois(DateTime aDate)
        {
            if (DateEntree == default(DateTime) || DateEntree > aDate)
                return 0;

            int mois = (aDate.Year - DateEntree.Year) * 12 + aDate.Month - DateEntree.Month;
            if (aDate.Day < DateEntree.Day)
                mois--;

            return Math.Max(0, mois % 12);
        }

        /// <summary>
        /// Indique si l'employé appartient à une catégorie supérieure
        /// </summary>
        public bool IsHigherCategory
        {
            get => _isHigherCategory;
            private set
            {
                if (_isHigherCategory != value)
                {
                    _isHigherCategory = value;
                    OnPropertyChanged();
                }
            }
        }


        /// <summary>
        /// Matricule CNSS du salarié (modules SAL-01 et DEC-04).
        ///
        /// Sans lui, la déclaration CNSS ne peut pas être produite : c'est ce numéro, et
        /// non le nom, qui identifie l'assuré dans les fichiers de la Caisse. La colonne
        /// accepte le vide — un salarié fraîchement embauché n'a pas encore son matricule —
        /// mais l'écran de déclaration signale alors la ligne comme incomplète.
        /// </summary>
        public string NumeroCnss
        {
            get => _numeroCnss;
            set
            {
                if (_numeroCnss != value)
                {
                    _numeroCnss = value;
                    OnPropertyChanged(nameof(NumeroCnss));
                }
            }
        }

        public string SituationMatrimoniale
        {
            get => _situationMatrimoniale;
            set
            {
                if (_situationMatrimoniale != value)
                {
                    _situationMatrimoniale = value;
                    OnPropertyChanged(nameof(SituationMatrimoniale));
                }
            }
        }



        // ══════════════════════════════════════════════════════════════════
        //  ÉTAT CIVIL, COORDONNÉES ET RÈGLEMENT (module SAL-01)
        //
        //  La fiche était jusqu'ici une fiche de CALCUL : juste ce qu'il faut pour
        //  produire un bulletin. Un dossier du personnel demande davantage — et pas
        //  par goût de l'exhaustivité : chacun de ces champs est réclamé par un
        //  document que l'entreprise doit produire. Le CNIB sur le contrat, l'adresse
        //  sur l'attestation de travail, le compte bancaire sur l'ordre de virement,
        //  la date de naissance sur la déclaration annuelle.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Date de naissance. Nulle tant qu'elle n'est pas connue.</summary>
        public DateTime? DateNaissance
        {
            get => _dateNaissance;
            set
            {
                if (_dateNaissance != value)
                {
                    _dateNaissance = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Age));
                }
            }
        }

        /// <summary>
        /// « NOM Prénom » — l'identification du salarié dans les listes déroulantes et les
        /// états. Les deux champs existaient déjà séparément ; les recoller à chaque
        /// appelant multipliait les variantes d'affichage.
        /// </summary>
        public string FullName => string.Format("{0} {1}", LastName, FirstName).Trim();

        /// <summary>
        /// « NOM Prénom (Matricule) » pour les listes déroulantes et sélecteurs de salariés.
        /// </summary>
        public string FullNameWithId
        {
            get
            {
                var matricule = !string.IsNullOrWhiteSpace(EmployeeId) ? EmployeeId : Matricule;
                var nom = FullName;
                if (string.IsNullOrWhiteSpace(nom)) return matricule ?? string.Empty;
                return string.IsNullOrWhiteSpace(matricule) ? nom : $"{nom} ({matricule})";
            }
        }

        /// <summary>Âge révolu, ou null si la date de naissance est inconnue.</summary>
        public int? Age
        {
            get
            {
                if (!DateNaissance.HasValue) return null;

                var naissance = DateNaissance.Value.Date;
                var aujourdhui = DateTime.Today;
                if (naissance > aujourdhui) return null;

                int age = aujourdhui.Year - naissance.Year;
                if (naissance > aujourdhui.AddYears(-age)) age--;
                return age;
            }
        }

        public string LieuNaissance
        {
            get => _lieuNaissance;
            set { if (_lieuNaissance != value) { _lieuNaissance = value; OnPropertyChanged(); } }
        }

        /// <summary>« Masculin » ou « Féminin ». Demandé par les états d'effectifs.</summary>
        public string Sexe
        {
            get => _sexe;
            set { if (_sexe != value) { _sexe = value; OnPropertyChanged(); } }
        }

        public string Nationalite
        {
            get => _nationalite;
            set { if (_nationalite != value) { _nationalite = value; OnPropertyChanged(); } }
        }

        /// <summary>Numéro de la carte nationale d'identité burkinabè.</summary>
        public string NumeroCnib
        {
            get => _numeroCnib;
            set { if (_numeroCnib != value) { _numeroCnib = value; OnPropertyChanged(); } }
        }

        public DateTime? DateDelivranceCnib
        {
            get => _dateDelivranceCnib;
            set { if (_dateDelivranceCnib != value) { _dateDelivranceCnib = value; OnPropertyChanged(); } }
        }

        public string LieuDelivranceCnib
        {
            get => _lieuDelivranceCnib;
            set { if (_lieuDelivranceCnib != value) { _lieuDelivranceCnib = value; OnPropertyChanged(); } }
        }

        public string Adresse
        {
            get => _adresse;
            set { if (_adresse != value) { _adresse = value; OnPropertyChanged(); } }
        }

        public string Telephone
        {
            get => _telephone;
            set { if (_telephone != value) { _telephone = value; OnPropertyChanged(); } }
        }

        public string Email
        {
            get => _email;
            set { if (_email != value) { _email = value; OnPropertyChanged(); } }
        }

        /// <summary>Identifiant fiscal unique du salarié, distinct de celui du dossier.</summary>
        public string NumeroIfu
        {
            get => _numeroIfu;
            set { if (_numeroIfu != value) { _numeroIfu = value; OnPropertyChanged(); } }
        }

        public string Banque
        {
            get => _banque;
            set { if (_banque != value) { _banque = value; OnPropertyChanged(); } }
        }

        /// <summary>Numéro de compte ou IBAN, selon ce que la banque communique.</summary>
        public string NumeroCompte
        {
            get => _numeroCompte;
            set
            {
                if (_numeroCompte != value)
                {
                    _numeroCompte = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CoordonneesBancairesCompletes));
                }
            }
        }

        /// <summary>Virement, espèces, chèque ou mobile money.</summary>
        public string ModeReglement
        {
            get => _modeReglement;
            set
            {
                if (_modeReglement != value)
                {
                    _modeReglement = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CoordonneesBancairesCompletes));
                }
            }
        }

        /// <summary>
        /// Vrai lorsque le règlement par virement est possible. Un salarié payé par
        /// virement mais sans compte renseigné est une paie qui ne partira pas — l'ordre
        /// de virement (CPT-02) s'appuiera sur ce contrôle.
        /// </summary>
        public bool CoordonneesBancairesCompletes
        {
            get
            {
                bool parVirement = string.Equals(ModeReglement, "Virement", StringComparison.OrdinalIgnoreCase);
                return !parVirement || !string.IsNullOrWhiteSpace(NumeroCompte);
            }
        }

        /// <summary>
        /// Plafond de l'indemnité de logement (20% du salaire brut, max 75 000 FCFA - Art.106 CGI)
        /// </summary>
        public decimal MaxHousingAllowance => Math.Min(BaseSalary * 0.2m, 75000m);

        /// <summary>
        /// Plafond de l'indemnité de fonction (5% du salaire brut, max 50 000 FCFA - Art.106 CGI)
        /// </summary>
        public decimal MaxFunctionAllowance => Math.Min(BaseSalary * 0.05m, 50000m);

        /// <summary>
        /// Plafond de l'indemnité de transport (5% du salaire brut, max 30 000 FCFA - Art.106 CGI)
        /// </summary>
        public decimal MaxTransportAllowance => Math.Min(BaseSalary * 0.05m, 30000m);

        /// <summary>
        /// Détermine si l'employé fait partie des catégories supérieures
        /// </summary>
        private void DetermineIfHigherCategory()
        {
            IsHigherCategory = EstCategorieCadreSuperieur(Category);
        }

        /// <summary>
        /// Vrai pour une catégorie de cadre supérieur, qui ouvre droit à l'abattement
        /// forfaitaire réduit (20 % au lieu de 25 %).
        ///
        /// Deux vocabulaires coexistent dans un dossier : la liste historique de la fiche
        /// (« Cadre Supérieur ») et les libellés des grilles conventionnelles (« Cadres
        /// Supérieurs »). Seul le premier était reconnu, si bien qu'un salarié classé avec
        /// le libellé de la grille — ce qu'exigent la prime d'ancienneté et le contrôle du
        /// minimum — perdait l'abattement de 20 %. La comparaison ignore désormais la
        /// casse, les accents, les espaces et le pluriel.
        /// </summary>
        public static bool EstCategorieCadreSuperieur(string categorie)
        {
            string c = NormaliserCategorie(categorie);
            return c == "cadre superieur" || c == "cadres superieurs" || c == "cadre superieure" || c == "cadres superieures";
        }

        /// <summary>
        /// Forme de comparaison d'un libellé de catégorie : minuscules, sans accents,
        /// espaces multiples réduits.
        /// </summary>
        public static string NormaliserCategorie(string categorie)
        {
            if (string.IsNullOrWhiteSpace(categorie)) return "";
            string d = categorie.Trim().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(d.Length);
            bool espace = false;
            foreach (char ch in d)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
                    continue;
                if (char.IsWhiteSpace(ch))
                {
                    if (!espace) sb.Append(' ');
                    espace = true;
                    continue;
                }
                espace = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Copie complète de l'employé.
        ///
        /// Les recopies champ par champ écrites à la main dans les vues omettaient
        /// systématiquement les propriétés ajoutées après coup — indemnité de sujétion,
        /// fonction, date d'entrée, situation matrimoniale — si bien qu'une simple
        /// modification de fiche les remettait à zéro. Passer par cette méthode garantit
        /// qu'aucune propriété ne sera plus perdue, y compris celles ajoutées plus tard.
        ///
        /// <see cref="IsHigherCategory"/> n'est pas recopiée : elle est recalculée
        /// automatiquement par l'affectation de <see cref="Category"/>.
        /// </summary>
        public Employee Clone()
        {
            return new Employee
            {
                EmployeeId = EmployeeId,
                FirstName = FirstName,
                LastName = LastName,
                BaseSalary = BaseSalary,
                HousingAllowance = HousingAllowance,
                TransportAllowance = TransportAllowance,
                FunctionAllowance = FunctionAllowance,
                SujetionAllowance = SujetionAllowance,
                FamilyCharges = FamilyCharges,
                IsSpouseWorking = IsSpouseWorking,
                PensionContribution = PensionContribution,
                Category = Category,
                Service = Service,
                Fonction = Fonction,
                DateEntree = DateEntree,
                SituationMatrimoniale = SituationMatrimoniale,
                NumeroCnss = NumeroCnss,
                DateNaissance = DateNaissance,
                LieuNaissance = LieuNaissance,
                Sexe = Sexe,
                Nationalite = Nationalite,
                NumeroCnib = NumeroCnib,
                DateDelivranceCnib = DateDelivranceCnib,
                LieuDelivranceCnib = LieuDelivranceCnib,
                Adresse = Adresse,
                Telephone = Telephone,
                Email = Email,
                NumeroIfu = NumeroIfu,
                Banque = Banque,
                NumeroCompte = NumeroCompte,
                ModeReglement = ModeReglement,
                ConventionId = ConventionId,
                Echelon = Echelon,
                DateSortie = DateSortie,
                MotifDepart = MotifDepart,
                StatutSalarie = StatutSalarie,
                PosteId = PosteId,
                ManagerMatricule = ManagerMatricule,
                SoldeCongesReprise = SoldeCongesReprise,
                DateRepriseConges = DateRepriseConges,
                TypeContrat = TypeContrat,
                PosteLibelle = PosteLibelle,
                ManagerNom = ManagerNom
            };
        }

        /// <summary>
        /// Identifiant du poste dans l'organigramme (module RH-01).
        /// </summary>
        public int? PosteId
        {
            get => _posteId;
            set { if (_posteId != value) { _posteId = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Matricule du supérieur hiérarchique direct N+1 (module RH-01).
        /// </summary>
        public string ManagerMatricule
        {
            get => _managerMatricule;
            set { if (_managerMatricule != value) { _managerMatricule = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Solde de congés repris à l'ouverture du dossier (jours restant dus à la date de
        /// reprise), pour un salarié déjà en poste avant l'utilisation du logiciel.
        ///
        /// Sans reprise, le compteur recomptait 2,5 jours par mois depuis la date d'entrée
        /// et ignorait tous les congés pris avant le logiciel : un salarié de vingt ans
        /// d'ancienneté affichait 600 jours dus (défaut n° 7 du guide de formation).
        /// </summary>
        public decimal SoldeCongesReprise
        {
            get => _soldeCongesReprise;
            set { if (_soldeCongesReprise != value) { _soldeCongesReprise = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Date d'arrêté du solde repris. Renseignée : le compteur = solde repris + droits
        /// acquis depuis cette date − congés pris depuis cette date. Vide : compteur depuis
        /// la date d'entrée, comme avant.
        /// </summary>
        public DateTime? DateRepriseConges
        {
            get => _dateRepriseConges;
            set { if (_dateRepriseConges != value) { _dateRepriseConges = value; OnPropertyChanged(); } }
        }

        private string _typeContrat;
        /// <summary>Type de contrat de travail (CDI, CDD, Stage...), repris de l'embauche RH-05.</summary>
        public string TypeContrat
        {
            get => _typeContrat;
            set { if (_typeContrat != value) { _typeContrat = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Intitulé du poste occupé (propriété d'affichage résolue depuis l'organigramme).
        /// </summary>
        public string PosteLibelle
        {
            get => _posteLibelle;
            set { if (_posteLibelle != value) { _posteLibelle = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Nom du supérieur hiérarchique direct (propriété d'affichage).
        /// </summary>
        public string ManagerNom
        {
            get => _managerNom;
            set { if (_managerNom != value) { _managerNom = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
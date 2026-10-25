using System;
using System.Linq;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Service de génération du bulletin de paie mensuel d'un employé, conforme au
    /// modèle de référence (fichier "model_buletin.xlsx", onglet "Option 4") et au
    /// moteur de calcul IUTS (<see cref="TaxCalculatorService.CalculateMonthlyTax"/>).
    /// </summary>
    public class BulletinService
    {
        private readonly TaxCalculatorService _taxCalculatorService;

        public BulletinService(TaxCalculatorService taxCalculatorService)
        {
            _taxCalculatorService = taxCalculatorService ?? throw new ArgumentNullException(nameof(taxCalculatorService));
        }

        /// <summary>
        /// Répercute sur le moteur le barème daté applicable à la période du bulletin
        /// (module DEC-02 sur le socle SOC-02). Passer <c>null</c> revient aux valeurs
        /// historiques du moteur.
        /// </summary>
        public void AppliquerBareme(BaremeLegal bareme)
        {
            _taxCalculatorService.AppliquerBareme(bareme);
        }

        /// <summary>
        /// Calcule le salaire net légal de référence (Brut imposable - CNSS salariale - IUTS)
        /// servant d'assiette au calcul de la quotité saisissable et cessible (module PAI-06).
        /// </summary>
        public decimal CalculerNetReference(Employee employee, ApportVariables apport = null)
        {
            if (employee == null) return 0m;
            var calcul = _taxCalculatorService.CalculateMonthlyTax(employee, apport ?? ApportVariables.Aucun());
            return Math.Max(0m, calcul.NetSalaryAfterTax - calcul.CnssEmployee - calcul.RetenueSpeciale);
        }

        /// <summary>
        /// Calcul produit par la dernière génération. C'est lui qu'il faut enregistrer
        /// dans l'historique pour que le bulletin existe ailleurs qu'à l'écran : les
        /// déclarations CNSS et IUTS, le livre de paie et les cumuls lisent tous cette
        /// table, pas les bulletins affichés.
        ///
        /// Ce service est instancié une fois par écran et utilisé séquentiellement ; la
        /// propriété n'est pas prévue pour un usage concurrent.
        /// </summary>
        public TaxCalculation DernierCalcul { get; private set; }

        /// <summary>
        /// Génère le bulletin de paie d'un employé pour une période (mois) donnée.
        /// </summary>
        /// <param name="employee">Employé concerné</param>
        /// <param name="periode">Une date quelconque du mois concerné (seul le mois/année compte)</param>
        /// <param name="congesPrisJours">Nombre de jours de congé pris pendant la période</param>
        /// <param name="modeReglement">Mode de règlement (ex. "Virement bancaire", "Espèces", "Chèque")</param>
        /// <param name="numeroCompte">Numéro de compte de règlement (optionnel)</param>
        /// <param name="apport">
        /// Variables du mois du salarié (PAI-02). Null — le cas par défaut — donne
        /// exactement le bulletin qu'on obtenait avant l'arrivée des variables.
        /// </param>
        public Bulletin GenererBulletin(
            Employee employee,
            DateTime periode,
            decimal congesPrisJours = 0m,
            string modeReglement = "Virement bancaire",
            string numeroCompte = null,
            ApportVariables apport = null)
        {
            if (employee == null) throw new ArgumentNullException(nameof(employee));

            var apportEffectif = apport ?? ApportVariables.Aucun();
            var calcul = _taxCalculatorService.CalculateMonthlyTax(employee, apportEffectif);

            // Le moteur date le calcul du jour où il tourne. Pour un bulletin, la seule
            // date qui vaille est celle de la PÉRIODE : un bulletin de septembre édité en
            // octobre reste un bulletin de septembre, et c'est à septembre qu'il doit être
            // déclaré. Sans cette ligne, la déclaration CNSS d'un mois ne trouve rien et
            // le verrou de clôture contrôle le mauvais mois.
            calcul.CalculationDate = new DateTime(periode.Year, periode.Month, 1)
                .AddMonths(1).AddDays(-1);

            DernierCalcul = calcul;

            // Congés pris : le compteur PAI-04 fait foi dès qu'une absence est enregistrée
            // pour ce mois. Le paramètre saisi à la main ne sert plus que de repli, pour
            // les dossiers qui n'ont pas encore basculé sur le compteur.
            if (apportEffectif.JoursCongesPris > 0m)
                congesPrisJours = apportEffectif.JoursCongesPris;

            DateTime periodeDebut = new DateTime(periode.Year, periode.Month, 1);
            DateTime periodeFin = periodeDebut.AddMonths(1).AddDays(-1);

            DateTime? dateEmbauche = employee.DateEntree != default(DateTime) ? employee.DateEntree : (DateTime?)null;
            int ancienneteMois = CalculerAncienneteMois(dateEmbauche, periodeFin);
            decimal congesAcquis = ancienneteMois * 2.5m;
            decimal congesCumules = congesAcquis - congesPrisJours;

            // IUTS brut (avant réduction pour charges familiales) : IUTSNet = IUTSBrut - AbattementChargesFamiliales
            decimal iutsBrut = calcul.IUTSNet + calcul.FamilyChargeReduction;

            // Informations de l'entreprise émettrice (dossier actuellement ouvert), affichées
            // dans l'en-tête du bulletin (à gauche), avec l'emplacement pour son logo.
            var dossier = DossierSession.Instance.CurrentDossier;

            // Cumuls depuis le 1er janvier : à défaut d'un historique de paie mensuel détaillé,
            // on estime le cumul en répétant la rémunération du mois en cours sur les mois
            // écoulés de l'année (bornés au mois d'embauche si l'employé n'a pas travaillé
            // toute l'année). Le bulletin indique clairement qu'il s'agit d'une estimation.
            int moisEcoulesAnnee = periodeFin.Month;
            if (dateEmbauche.HasValue && dateEmbauche.Value.Year == periodeFin.Year)
                moisEcoulesAnnee = periodeFin.Month - dateEmbauche.Value.Month + 1;
            moisEcoulesAnnee = Math.Max(1, moisEcoulesAnnee);

            decimal cumulBrutAnnuel = calcul.SalaireBrut * moisEcoulesAnnee;
            decimal cumulIUTSAnnuel = calcul.IUTSNet * moisEcoulesAnnee;
            decimal cumulCnssEmployeAnnuel = calcul.CnssEmployee * moisEcoulesAnnee;
            decimal cumulNetAnnuel = calcul.SalaireNetaPayer * moisEcoulesAnnee;

            // Numéro de contrôle unique du bulletin (protection contre les faux bulletins) :
            // calculé à partir des données visibles sur le document, il peut être recalculé
            // à tout moment pour vérifier qu'un bulletin n'a pas été falsifié.
            DateTime dateGeneration = DateTime.Now;
            string numeroControle = QrCodeService.GenererNumeroControle(
                $"{employee.EmployeeId}|{periodeFin:yyyyMM}|{calcul.SalaireNetaPayer}|{dateGeneration:yyyyMMddHHmmss}");

            return new Bulletin
            {
                EntrepriseRaisonSociale = dossier?.RaisonSociale,
                EntrepriseAdresse = dossier?.Adresse,
                EntrepriseVille = dossier?.Ville,
                EntrepriseTelephone = dossier?.Telephone,
                EntrepriseIFU = dossier?.IFU,
                EntrepriseRCCM = dossier?.RCCM,
                EntrepriseRegimeFiscal = dossier?.RegimeFiscal,
                EntrepriseLogoPath = dossier?.LogoPath,

                EmployeeId = employee.EmployeeId,
                NomEmploye = employee.LastName,
                PrenomEmploye = employee.FirstName,
                Emploi = employee.Fonction,
                Categorie = employee.Category,
                DateEmbauche = dateEmbauche,
                PeriodeDebut = periodeDebut,
                PeriodeFin = periodeFin,
                AncienneteMois = ancienneteMois,
                CongesAcquisJours = congesAcquis,
                CongesPrisJours = congesPrisJours,
                CongesCumulesJours = congesCumules,
                ChargesFamiliales = employee.FamilyCharges,
                ConjointTravaille = employee.IsSpouseWorking,

                SalaireDeBase = employee.BaseSalary,
                IndemniteLogement = employee.HousingAllowance,
                IndemniteTransport = employee.TransportAllowance,
                IndemniteFonction = employee.FunctionAllowance,
                IndemniteSujetion = employee.SujetionAllowance,
                SalaireBrut = calcul.SalaireBrut,

                // Variables du mois (PAI-02) : le détail suit le calcul sur le bulletin,
                // sans quoi le brut ne se décomposerait plus.
                LignesVariables = apportEffectif.Lignes
                    .Select(l => new LigneBulletinVariable
                    {
                        Code = l.Code,
                        Libelle = l.LibelleBulletin,
                        Montant = l.Montant,
                        EstRetenue = l.Sens == SensRubrique.Retenue,
                        Traitement = l.TraitementLibelle
                    })
                    .ToList(),
                TotalGainsVariables = apportEffectif.TotalGainsLignes,
                TotalRetenuesDiverses = apportEffectif.TotalRetenuesLignes,

                // Absences non rémunérées (PAI-04) : déjà déduites du brut par le moteur,
                // reprises ici pour que la ligne apparaisse et explique l'écart.
                RetenueAbsences = calcul.RetenueAbsences,
                LibelleRetenueAbsences = string.IsNullOrWhiteSpace(apportEffectif.LibelleRetenueSurBrut)
                    ? "Absence non rémunérée"
                    : apportEffectif.LibelleRetenueSurBrut,

                CnssEmploye = calcul.CnssEmployee,
                CnssEmployeur = calcul.CnssPartPatronale,
                TotalExonerations = calcul.ExemptAmount,
                AbattementForfaitaire = calcul.ProfessionalDeduction,
                BaseIUTS = calcul.BaseIUTSHorsDeduction,
                IUTSBrut = iutsBrut,
                AbattementChargesFamiliales = calcul.FamilyChargeReduction,
                NetIUTS = calcul.IUTSNet,
                RetenueObligatoire = calcul.RetenueSpeciale,

                SalaireNetAPayer = calcul.SalaireNetaPayer,

                ModeReglement = modeReglement,
                NumeroCompte = numeroCompte,

                CumulBrutAnnuel = cumulBrutAnnuel,
                CumulIUTSAnnuel = cumulIUTSAnnuel,
                CumulCnssEmployeAnnuel = cumulCnssEmployeAnnuel,
                CumulNetAnnuel = cumulNetAnnuel,
                CumulsEstimes = true,

                NumeroControle = numeroControle,
                DateGeneration = dateGeneration
            };
        }

        /// <summary>
        /// Calcule l'ancienneté en mois complets entre la date d'embauche et la date de référence.
        /// Retourne 0 si la date d'embauche est inconnue ou postérieure à la date de référence.
        /// </summary>
        private int CalculerAncienneteMois(DateTime? dateEmbauche, DateTime dateReference)
        {
            if (dateEmbauche == null || dateEmbauche.Value > dateReference)
                return 0;

            var d = dateEmbauche.Value;
            int mois = (dateReference.Year - d.Year) * 12 + (dateReference.Month - d.Month);
            if (dateReference.Day < d.Day)
                mois--;

            return Math.Max(0, mois);
        }
    }
}

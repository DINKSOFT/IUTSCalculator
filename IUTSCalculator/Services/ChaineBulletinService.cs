using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Refus de remplacer un bulletin de sortie (solde de tout compte validé) par une paie
    /// ordinaire. Dérive d'InvalidOperationException comme le verrou de clôture, mais ne
    /// concerne qu'un salarié : un traitement de lot passe au suivant.
    /// </summary>
    public class BulletinDeSortieProtegeException : InvalidOperationException
    {
        public BulletinDeSortieProtegeException(string message) : base(message) { }
    }

    /// <summary>Résultat de la préparation, puis de l'enregistrement, du bulletin d'un mois.</summary>
    public sealed class ResultatBulletinMois
    {
        /// <summary>Vrai pour le bulletin de sortie préparé par le solde de tout compte.</summary>
        public bool EstBulletinDeSortie { get; set; }

        public Bulletin Bulletin { get; set; }
        public TaxCalculation Calcul { get; set; }
        public ApportVariables Apport { get; set; }

        /// <summary>Vrai une fois le bulletin entré dans la paie du mois (avec son archive).</summary>
        public bool Enregistre { get; set; }

        /// <summary>Motif du refus d'enregistrement (période clôturée…), sinon null.</summary>
        public string Refus { get; set; }

        /// <summary>Avertissements de l'apport (doublons possibles…), pour l'écran.</summary>
        public List<string> Avertissements { get; } = new List<string>();
    }

    /// <summary>
    /// Chaîne unique de préparation et d'enregistrement d'un bulletin mensuel.
    ///
    /// Quatre écrans calculaient la paie d'un mois, chacun avec sa propre copie de
    /// l'assemblage : « Bulletin de paie », « Bulletins en masse », « Calcul en lot » et
    /// « Calcul d'IUTS ». Les deux derniers n'appliquaient pas les saisies sur salaire et
    /// enregistraient le calcul SANS archive du bulletin — un bulletin archivé du mois
    /// était remplacé par un simple calcul, que « Rappeler » ne savait plus réafficher
    /// (défaut n° 4 du guide de formation). Ils passent désormais par cette chaîne, qui
    /// reprend l'ordre d'assemblage de l'écran « Bulletin de paie » :
    /// variables → absences et prorata → prêts → profil → heures supplémentaires →
    /// convention → rappels → avantages en nature → saisies sur le net.
    ///
    /// L'enregistrement archive le bulletin à côté de son calcul, enregistre les retenues
    /// de saisies du mois et marque les rappels imputés — exactement comme « Bulletin de
    /// paie ». Le verrou de clôture s'applique (InvalidOperationException).
    /// </summary>
    public class ChaineBulletinService
    {
        private readonly DataService _dataService;
        private readonly BulletinService _moteur;
        private readonly BaremeService _baremeService;
        private readonly RubriqueService _rubriqueService;
        private readonly VariablePaieService _variablePaieService;
        private readonly AbsenceService _absenceService;
        private readonly PretService _pretService;
        private readonly ProfilPaieService _profilPaieService;
        private readonly HeureSupplementaireService _heureSupplementaireService;
        private readonly ConventionCollectiveService _conventionCollectiveService;
        private readonly RappelPaieService _rappelPaieService;
        private readonly AvantageNatureService _avantageNatureService;
        private readonly SaisieOppositionService _saisieOppositionService;

        public ChaineBulletinService(
            DataService dataService,
            BulletinService moteur,
            BaremeService baremeService = null,
            RubriqueService rubriqueService = null,
            VariablePaieService variablePaieService = null,
            AbsenceService absenceService = null,
            PretService pretService = null,
            ProfilPaieService profilPaieService = null,
            HeureSupplementaireService heureSupplementaireService = null,
            ConventionCollectiveService conventionCollectiveService = null,
            RappelPaieService rappelPaieService = null,
            AvantageNatureService avantageNatureService = null,
            SaisieOppositionService saisieOppositionService = null)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _moteur = moteur ?? throw new ArgumentNullException(nameof(moteur));
            _baremeService = baremeService;
            _rubriqueService = rubriqueService;
            _variablePaieService = variablePaieService;
            _absenceService = absenceService;
            _pretService = pretService;
            _profilPaieService = profilPaieService;
            _heureSupplementaireService = heureSupplementaireService;
            _conventionCollectiveService = conventionCollectiveService;
            _rappelPaieService = rappelPaieService;
            _avantageNatureService = avantageNatureService;
            _saisieOppositionService = saisieOppositionService;
        }

        /// <summary>Moteur de bulletin utilisé par la chaîne (barème du mois appliqué).</summary>
        public BulletinService Moteur => _moteur;

        /// <summary>Applique au moteur le barème daté de la période.</summary>
        public async Task AppliquerBaremeAsync(DateTime periode, Action<string> signaler = null)
        {
            if (_baremeService == null) return;
            try
            {
                var bareme = await _baremeService.BaremeApplicableAsync(periode);
                _moteur.AppliquerBareme(bareme);
            }
            catch (Exception ex)
            {
                signaler?.Invoke("Barème illisible, calcul avec les valeurs historiques : " + ex.Message);
                _moteur.AppliquerBareme(null);
            }
        }

        /// <summary>
        /// Assemble l'apport du mois d'un salarié (fiche passée telle quelle : l'appelant la
        /// relit en base, ou fournit la fiche modifiée à l'écran). Une source illisible est
        /// signalée et ignorée, comme sur l'écran « Bulletin de paie ».
        /// </summary>
        public async Task<ApportVariables> ChargerApportAsync(
            Employee employe, DateTime periode, Action<string> signaler = null, bool avecBareme = true,
            bool avecSaisies = true)
        {
            if (employe == null) throw new ArgumentNullException(nameof(employe));
            if (avecBareme) await AppliquerBaremeAsync(periode, signaler);

            string employeeId = employe.EmployeeId;
            int annee = periode.Year, mois = periode.Month;
            var apport = ApportVariables.Aucun();
            List<Rubrique> plan = null;

            if (_rubriqueService != null)
            {
                try { plan = await _rubriqueService.ChargerAsync(); }
                catch (Exception ex) { signaler?.Invoke("Plan de paie illisible : " + ex.Message); }
            }

            if (_variablePaieService != null && plan != null && !string.IsNullOrWhiteSpace(employeeId))
            {
                try { apport = await _variablePaieService.ApportAsync(annee, mois, employeeId, plan); }
                catch (Exception ex) { signaler?.Invoke("Variables du mois illisibles, calcul sans elles : " + ex.Message); }
            }

            if (_absenceService != null)
            {
                try
                {
                    var reglages = await _dataService.LoadSettingsAsync();
                    var baseRetenue = reglages != null ? reglages.BaseRetenueAbsence : BaseRetenueAbsence.TrentiemeMensuel;
                    apport = apport.AvecAbsences(await _absenceService.ApportAsync(employe, annee, mois, baseRetenue));
                }
                catch (Exception ex) { signaler?.Invoke("Absences illisibles, calcul sans elles : " + ex.Message); }
            }

            if (_pretService != null && !string.IsNullOrWhiteSpace(employeeId))
            {
                try { apport = apport.AvecPrets(await _pretService.ApportAsync(employeeId, annee, mois)); }
                catch (Exception ex) { signaler?.Invoke("Prêts illisibles, calcul sans retenue de remboursement : " + ex.Message); }
            }

            if (_profilPaieService != null && plan != null)
            {
                try { apport = apport.AvecProfil(await _profilPaieService.ApportAsync(employe, annee, mois, plan)); }
                catch (Exception ex) { signaler?.Invoke("Profil de paie illisible, calcul sans lui : " + ex.Message); }
            }

            if (_heureSupplementaireService != null)
            {
                try { apport = apport.AvecHeuresSupplementaires(await _heureSupplementaireService.ApportAsync(employe, annee, mois)); }
                catch (Exception ex) { signaler?.Invoke("Heures supplémentaires illisibles, calcul sans elles : " + ex.Message); }
            }

            if (_conventionCollectiveService != null)
            {
                try { apport = apport.AvecConventionCollective(await _conventionCollectiveService.ApportAsync(employe, periode)); }
                catch (Exception ex) { signaler?.Invoke("Convention collective illisible, calcul sans prime d'ancienneté : " + ex.Message); }
            }

            if (_rappelPaieService != null && !string.IsNullOrWhiteSpace(employeeId))
            {
                try { apport = apport.AvecRappels(await _rappelPaieService.ApportAsync(annee, mois, employeeId)); }
                catch (Exception ex) { signaler?.Invoke("Rappels et régularisations illisibles, calcul sans eux : " + ex.Message); }
            }

            if (_avantageNatureService != null && !string.IsNullOrWhiteSpace(employeeId))
            {
                try { apport = apport.AvecAvantagesNature(await _avantageNatureService.ObtenirApportMoisAsync(employeeId, annee, mois)); }
                catch (Exception ex) { signaler?.Invoke("Avantages en nature illisibles, calcul sans eux : " + ex.Message); }
            }

            // Saisies-arrêts, cessions et oppositions : sur le net, en dernier. Un appelant
            // qui ajoute encore des lignes (bulletin de sortie) les calcule ensuite avec
            // AjouterSaisiesAsync, sur le net qui inclut ces lignes.
            if (avecSaisies)
                apport = await AjouterSaisiesAsync(apport, employe, periode, signaler);

            return apport;
        }

        /// <summary>
        /// Ajoute à l'apport les saisies du mois, calculées sur le net de référence de CET
        /// apport (brut − IUTS − CNSS − retenue obligatoire), et retient ce net dans
        /// <see cref="ApportVariables.NetReferenceSaisies"/> : l'enregistrement des retenues
        /// réutilise exactement la même base que la ligne imprimée sur le bulletin.
        /// À appeler en DERNIER, une fois tous les gains connus.
        /// </summary>
        public async Task<ApportVariables> AjouterSaisiesAsync(
            ApportVariables apport, Employee employe, DateTime periode, Action<string> signaler = null)
        {
            if (apport == null || employe == null || _saisieOppositionService == null || string.IsNullOrWhiteSpace(employe.EmployeeId))
                return apport;

            try
            {
                decimal netRef = _moteur.CalculerNetReference(employe, apport);
                var saisies = netRef > 0m
                    ? await _saisieOppositionService.ApportAsync(periode.Year, periode.Month, employe.EmployeeId, netRef)
                    : null;
                apport = apport.AvecSaisies(saisies, netRef);
            }
            catch (Exception ex) { signaler?.Invoke("Saisies illisibles, calcul sans retenue judiciaire : " + ex.Message); }

            return apport;
        }

        /// <summary>
        /// Prépare le bulletin du mois (barème, apport, bulletin) sans rien enregistrer.
        /// <paramref name="apport"/> fourni : utilisé tel quel (le barème est quand même appliqué).
        /// </summary>
        public async Task<ResultatBulletinMois> PreparerAsync(
            Employee employe, DateTime periode, decimal congesPrisJours = 0m,
            string modeReglement = ModesReglementPaie.ModeDeLaFiche, string numeroCompte = null,
            ApportVariables apport = null, Action<string> signaler = null)
        {
            if (employe == null) throw new ArgumentNullException(nameof(employe));

            if (apport == null)
                apport = await ChargerApportAsync(employe, periode, signaler, avecBareme: true);
            else
                await AppliquerBaremeAsync(periode, signaler);

            var resultat = new ResultatBulletinMois { Apport = apport };
            // Bulletin et calcul rendus ensemble : aucun état partagé du moteur (DernierCalcul)
            // ne peut mêler deux préparations concurrentes.
            var genere = await Task.Run(() => _moteur.GenererBulletinEtCalcul(
                employe, periode, congesPrisJours, modeReglement, numeroCompte, apport));
            resultat.Bulletin = genere.Bulletin;
            resultat.Calcul = genere.Calcul;
            resultat.Avertissements.AddRange(apport.Avertissements);
            return resultat;
        }

        /// <summary>
        /// Enregistre un bulletin préparé : calcul + archive du document, retenues de
        /// saisies du mois, rappels marqués imputés. Une période clôturée lève
        /// InvalidOperationException (rien n'est écrit).
        /// </summary>
        public async Task EnregistrerAsync(ResultatBulletinMois resultat, Employee employe, DateTime periode)
        {
            if (resultat == null || resultat.Calcul == null) throw new ArgumentNullException(nameof(resultat));
            if (employe == null) throw new ArgumentNullException(nameof(employe));

            // Le bulletin du mois de sortie appartient au solde de tout compte validé : une
            // paie ordinaire (sans indemnités de rupture) ne doit pas l'écraser.
            if (!resultat.EstBulletinDeSortie)
            {
                int? stc = await _dataService.SoldeToutCompteDuMoisAsync(employe.EmployeeId, periode);
                if (stc.HasValue)
                {
                    throw new BulletinDeSortieProtegeException(string.Format(
                        "{0} {1} a un solde de tout compte validé (n° {2}) pour {3:MM/yyyy} : le bulletin de ce mois est son " +
                        "bulletin de sortie, il ne se recalcule que depuis l'écran « Solde de tout compte » " +
                        "(supprimez le décompte archivé puis validez-le à nouveau). Rien n'a été enregistré.",
                        employe.FirstName, employe.LastName, stc.Value, periode));
                }
            }

            await _dataService.EnregistrerBulletinDuMoisAsync(resultat.Calcul, periode, resultat.Bulletin);
            resultat.Enregistre = true;

            if (_saisieOppositionService != null)
            {
                try
                {
                    // Même base que la retenue imprimée sur le bulletin (fixée au calcul des saisies).
                    decimal netRef = resultat.Apport != null && resultat.Apport.NetReferenceSaisies.HasValue
                        ? resultat.Apport.NetReferenceSaisies.Value
                        : _moteur.CalculerNetReference(employe, resultat.Apport);
                    await _saisieOppositionService.AppliquerEtEnregistrerRetenuesMoisAsync(
                        periode.Year, periode.Month, employe.EmployeeId, netRef, DateTime.Today);
                }
                catch (Exception ex)
                {
                    resultat.Avertissements.Add("suivi des saisies non mis à jour : " + ex.Message);
                }
            }

            if (_rappelPaieService != null)
            {
                try
                {
                    await _rappelPaieService.MarquerImputesMoisAsync(periode.Year, periode.Month, employe.EmployeeId);
                }
                catch (Exception ex)
                {
                    resultat.Avertissements.Add("rappels non marqués imputés : " + ex.Message);
                }
            }
        }

        /// <summary>Prépare puis enregistre ; un refus de clôture est rendu dans <see cref="ResultatBulletinMois.Refus"/>.</summary>
        public async Task<ResultatBulletinMois> GenererEtEnregistrerAsync(
            Employee employe, DateTime periode, decimal congesPrisJours = 0m,
            string modeReglement = ModesReglementPaie.ModeDeLaFiche, string numeroCompte = null,
            ApportVariables apport = null, Action<string> signaler = null)
        {
            var resultat = await PreparerAsync(employe, periode, congesPrisJours, modeReglement, numeroCompte, apport, signaler);
            try
            {
                await EnregistrerAsync(resultat, employe, periode);
            }
            catch (InvalidOperationException ex)
            {
                resultat.Refus = ex.Message;
            }
            return resultat;
        }
    }
}

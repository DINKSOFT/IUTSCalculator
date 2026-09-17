using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Construit l'état annuel des salaires — DADS (module DEC-05).
    ///
    /// Le service ne calcule pas la paie : il lit les bulletins déjà enregistrés sur
    /// les mois CLÔTURÉS de l'année (module PAI-01), les rapproche des salariés et du
    /// plan de paie, et cumule. Même principe que <see cref="LivrePaieService"/>, dont
    /// il reprend directement la lecture de <c>TaxCalculations</c> et de l'archive du
    /// bulletin (<c>BulletinArchive</c>) pour le détail par rubrique — mais sur les
    /// douze mois de l'année plutôt qu'un seul, et seulement pour les mois arrêtés :
    /// un mois jamais ouvert ou encore ouvert n'a pas de totaux figés, il ne doit donc
    /// pas entrer dans un cumul annuel destiné à la déclaration.
    /// </summary>
    public class EtatAnnuelSalairesService
    {
        private readonly SQLiteService _sqliteService;
        private readonly PeriodePaieService _periodeService;
        private readonly RubriqueService _rubriqueService;

        public EtatAnnuelSalairesService(
            SQLiteService sqliteService,
            PeriodePaieService periodeService,
            RubriqueService rubriqueService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            if (periodeService == null) throw new ArgumentNullException("periodeService");
            if (rubriqueService == null) throw new ArgumentNullException("rubriqueService");

            _sqliteService = sqliteService;
            _periodeService = periodeService;
            _rubriqueService = rubriqueService;
        }

        /// <summary>
        /// Construit l'état annuel d'une année. Ne lève jamais faute de mois clôturé :
        /// l'état revient avec une liste de lignes vide et <c>MoisManquants</c> égal
        /// aux douze mois — c'est à l'écran de le dire clairement (voir
        /// <see cref="EtatAnnuelSalaires.Anomalies"/>).
        /// </summary>
        public async Task<EtatAnnuelSalaires> ConstruireAsync(Dossier dossier, int annee)
        {
            if (dossier == null) throw new ArgumentNullException("dossier");

            var etat = new EtatAnnuelSalaires
            {
                RaisonSociale = dossier.RaisonSociale,
                Ifu = dossier.IFU,
                Rccm = dossier.RCCM,
                Adresse = dossier.Adresse,
                Ville = dossier.Ville,
                Telephone = dossier.Telephone,
                Annee = annee
            };

            // Quels mois sont réellement arrêtés : c'est la définition même de ce
            // module, pas une simple préférence d'affichage.
            var moisClotures = new List<int>();
            var moisManquants = new List<int>();
            for (int mois = 1; mois <= 12; mois++)
            {
                var periode = await _periodeService.LirePeriodeAsync(annee, mois);
                if (periode != null && periode.EstCloturee)
                    moisClotures.Add(mois);
                else
                    moisManquants.Add(mois);
            }

            etat.MoisClotures = moisClotures;
            etat.MoisManquants = moisManquants;

            if (moisClotures.Count == 0)
                return etat;

            var rubriques = await _rubriqueService.ChargerAsync();
            var rubriqueParCode = new Dictionary<string, Rubrique>(StringComparer.OrdinalIgnoreCase);
            if (rubriques != null)
            {
                foreach (var r in rubriques)
                {
                    if (!rubriqueParCode.ContainsKey(r.Code))
                        rubriqueParCode[r.Code] = r;
                }
            }

            await RemplirLignesAsync(etat, moisClotures, rubriqueParCode);

            return etat;
        }

        /// <summary>
        /// Une ligne par salarié, cumulée sur les mois clôturés — depuis les
        /// TaxCalculations et l'archive du bulletin, exactement comme
        /// <see cref="LivrePaieService"/>.
        /// </summary>
        private async Task RemplirLignesAsync(
            EtatAnnuelSalaires etat, List<int> moisClotures, Dictionary<string, Rubrique> rubriqueParCode)
        {
            var moisFermes = new HashSet<int>(moisClotures);
            var codesRubriques = new Dictionary<string, ColonneRubriqueDynamique>(StringComparer.OrdinalIgnoreCase);
            var lignesParEmploye = new Dictionary<string, LigneEtatAnnuelSalaires>(StringComparer.OrdinalIgnoreCase);
            var ordreEmployes = new List<string>();

            var debutAnnee = new DateTime(etat.Annee, 1, 1);
            var finAnnee = new DateTime(etat.Annee, 12, 31, 23, 59, 59);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT c.EmployeeId,
                           c.CalculationDate,
                           c.SalaireBrut,
                           c.BaseIUTSHorsDeduction,
                           c.IUTSNet,
                           c.CnssEmployee,
                           c.CnssPartPatronale,
                           c.TaxePatronale,
                           c.SalaireNetaPayer,
                           c.BulletinArchive,
                           e.FirstName,
                           e.LastName,
                           e.Category,
                           e.Fonction,
                           e.DateEntree,
                           e.NumeroCnss
                    FROM TaxCalculations c
                    LEFT JOIN Employees e ON e.EmployeeId = c.EmployeeId
                    WHERE c.CalculationDate >= @debut
                      AND c.CalculationDate <= @fin
                      AND c.IsAnnual = 0
                      AND c.IsRegularization = 0
                    ORDER BY e.LastName, e.FirstName, c.CalculationDate";

                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(debutAnnee));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(finAnnee));

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        DateTime dateCalcul = DbValue.DateFromDb(reader["CalculationDate"], debutAnnee);

                        // Le mois de CE bulletin n'est pas clôturé : il n'entre pas dans le
                        // cumul, même si sa date tombe dans l'année demandée.
                        if (!moisFermes.Contains(dateCalcul.Month))
                            continue;

                        string employeeId = DbValue.TextFromDb(reader["EmployeeId"]) ?? "";

                        LigneEtatAnnuelSalaires ligne;
                        if (!lignesParEmploye.TryGetValue(employeeId, out ligne))
                        {
                            ligne = new LigneEtatAnnuelSalaires
                            {
                                Matricule = employeeId,
                                Nom = DbValue.TextFromDb(reader["LastName"]),
                                Prenom = DbValue.TextFromDb(reader["FirstName"]),
                                Categorie = DbValue.TextFromDb(reader["Category"]),
                                Emploi = DbValue.TextFromDb(reader["Fonction"]),
                                NumeroCnss = DbValue.TextFromDb(reader["NumeroCnss"]),
                                DateEntree = DbValue.DateNullableFromDb(reader["DateEntree"])
                            };
                            lignesParEmploye[employeeId] = ligne;
                            ordreEmployes.Add(employeeId);
                        }

                        ligne.NombreBulletins++;
                        ligne.CumulSalaireBrut += DbValue.MoneyFromDb(reader["SalaireBrut"]);
                        ligne.CumulBaseImposable += DbValue.MoneyFromDb(reader["BaseIUTSHorsDeduction"]);
                        ligne.CumulIuts += DbValue.MoneyFromDb(reader["IUTSNet"]);
                        ligne.CumulCnssSalariale += DbValue.MoneyFromDb(reader["CnssEmployee"]);
                        ligne.CumulCnssPatronale += DbValue.MoneyFromDb(reader["CnssPartPatronale"]);
                        ligne.CumulTpa += DbValue.MoneyFromDb(reader["TaxePatronale"]);
                        ligne.CumulNetAPayer += DbValue.MoneyFromDb(reader["SalaireNetaPayer"]);

                        // Tenter de lire le BulletinArchive pour le détail par rubrique,
                        // exactement comme LivrePaieService.
                        string archiveJson = DbValue.TextFromDb(reader["BulletinArchive"]);
                        if (!string.IsNullOrWhiteSpace(archiveJson))
                        {
                            try
                            {
                                var bulletin = JsonSerializer.Deserialize<Bulletin>(archiveJson);
                                if (bulletin != null && bulletin.LignesVariables != null)
                                {
                                    foreach (var lv in bulletin.LignesVariables)
                                    {
                                        string code = lv.Code ?? "";
                                        if (string.IsNullOrWhiteSpace(code)) continue;

                                        decimal montant = lv.EstRetenue ? -lv.Montant : lv.Montant;

                                        if (ligne.MontantsParRubrique.ContainsKey(code))
                                            ligne.MontantsParRubrique[code] += montant;
                                        else
                                            ligne.MontantsParRubrique[code] = montant;

                                        if (!codesRubriques.ContainsKey(code))
                                        {
                                            int ordre = 999;
                                            bool estRetenue = lv.EstRetenue;
                                            string libelle = lv.Libelle ?? code;

                                            Rubrique rub;
                                            if (rubriqueParCode.TryGetValue(code, out rub))
                                            {
                                                ordre = rub.Ordre;
                                                libelle = rub.Libelle;
                                                estRetenue = rub.Sens == SensRubrique.Retenue;
                                            }

                                            codesRubriques[code] = new ColonneRubriqueDynamique
                                            {
                                                Code = code,
                                                Libelle = libelle,
                                                Ordre = ordre,
                                                EstRetenue = estRetenue
                                            };
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Archive illisible pour ce bulletin : le détail par rubrique de
                                // cet employé sera incomplet pour ce mois, mais les cumuls de
                                // synthèse (brut, base imposable, IUTS, cotisations) restent
                                // exacts — ils ne dépendent pas de l'archive.
                            }
                        }
                    }
                }
            }

            foreach (var employeeId in ordreEmployes)
                etat.Lignes.Add(lignesParEmploye[employeeId]);

            etat.ColonnesDynamiques = codesRubriques.Values
                .OrderBy(c => c.Ordre)
                .ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}

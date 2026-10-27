using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Accès au plan de paie du dossier (module SOC-01).
    ///
    /// À la première ouverture, le plan est alimenté à partir des éléments que le moteur
    /// de calcul gère déjà : salaire de base, quatre indemnités avec leurs plafonds de
    /// l'article 106 du CGI, cotisation CNSS salariale et IUTS. Ces rubriques-là sont
    /// marquées « système » — leur code est figé puisque le calcul s'y réfère — mais leur
    /// libellé, leur ordre d'impression et leurs comptes comptables restent modifiables,
    /// et toute rubrique nouvelle se crée librement à côté.
    /// </summary>
    public class RubriqueService
    {
        private readonly SQLiteService _sqliteService;

        private const string Colonnes =
            "Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal, CodeBase, " +
            "SoumisIUTS, SoumisCNSS, SoumisTPA, Plafonne, Plafond, PourcentagePlafond, Ordre, Active, " +
            "EstSysteme, CompteComptable, CompteContrepartie, Commentaire";

        // Codes reconnus par le moteur de calcul : ne pas renommer.
        public const string CodeSalaireBase = "SALBASE";
        public const string CodeIndemniteLogement = "INDLOG";
        public const string CodeIndemniteTransport = "INDTRA";
        public const string CodeIndemniteFonction = "INDFON";
        public const string CodeIndemniteSujetion = "INDSUJ";
        public const string CodeCnssSalarie = "CNSSSAL";
        public const string CodeIuts = "IUTS";

        public RubriqueService(SQLiteService sqliteService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            _sqliteService = sqliteService;
        }

        /// <summary>
        /// Charge le plan de paie, en l'alimentant d'abord s'il est vide — c'est le cas
        /// d'un dossier créé avant l'arrivée de ce module.
        /// </summary>
        public async Task<List<Rubrique>> ChargerAsync()
        {
            var rubriques = await LireAsync();

            if (rubriques.Count == 0)
            {
                await EnregistrerToutAsync(PlanDePaieInitial());
                rubriques = await LireAsync();
            }

            return rubriques;
        }

        private async Task<List<Rubrique>> LireAsync()
        {
            var liste = new List<Rubrique>();

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + Colonnes + " FROM Rubriques ORDER BY Ordre, Code";
                using (var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        liste.Add(Lire(reader));
                }
            }

            return liste;
        }

        private static Rubrique Lire(SqliteDataReader reader)
        {
            return new Rubrique
            {
                Code = DbValue.TextFromDb(reader["Code"]),
                Libelle = DbValue.TextFromDb(reader["Libelle"]),
                Sens = (SensRubrique)DbValue.IntFromDb(reader["Sens"]),
                ModeCalcul = (ModeCalculRubrique)DbValue.IntFromDb(reader["ModeCalcul"]),
                MontantFixe = DbValue.MoneyFromDb(reader["MontantFixe"]),
                TauxSalarial = DbValue.DecimalFromDb(reader["TauxSalarial"]),
                TauxPatronal = DbValue.DecimalFromDb(reader["TauxPatronal"]),
                CodeBase = DbValue.TextFromDb(reader["CodeBase"]),
                SoumisIUTS = DbValue.IntFromDb(reader["SoumisIUTS"]) == 1,
                SoumisCNSS = DbValue.IntFromDb(reader["SoumisCNSS"]) == 1,
                SoumisTPA = DbValue.IntFromDb(reader["SoumisTPA"]) == 1,
                Plafonne = DbValue.IntFromDb(reader["Plafonne"]) == 1,
                Plafond = DbValue.MoneyFromDb(reader["Plafond"]),
                PourcentagePlafond = DbValue.DecimalFromDb(reader["PourcentagePlafond"]),
                Ordre = DbValue.IntFromDb(reader["Ordre"]),
                Active = DbValue.IntFromDb(reader["Active"]) == 1,
                EstSysteme = DbValue.IntFromDb(reader["EstSysteme"]) == 1,
                CompteComptable = DbValue.TextFromDb(reader["CompteComptable"]),
                CompteContrepartie = DbValue.TextFromDb(reader["CompteContrepartie"]),
                Commentaire = DbValue.TextFromDb(reader["Commentaire"])
            };
        }

        public async Task EnregistrerAsync(Rubrique rubrique)
        {
            if (rubrique == null) throw new ArgumentNullException("rubrique");
            if (string.IsNullOrWhiteSpace(rubrique.Code))
                throw new InvalidOperationException("Le code de la rubrique est obligatoire.");

            using (var conn = _sqliteService.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                await EcrireAsync(rubrique, conn, tx);
                tx.Commit();
            }
        }

        public async Task EnregistrerToutAsync(IEnumerable<Rubrique> rubriques)
        {
            if (rubriques == null) return;

            using (var conn = _sqliteService.CreerConnexion())
            using (var tx = conn.BeginTransaction())
            {
                foreach (var r in rubriques)
                    await EcrireAsync(r, conn, tx);
                tx.Commit();
            }
        }

        private static async Task EcrireAsync(Rubrique r, SqliteConnection conn, SqliteTransaction tx)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO Rubriques
                    (Code, Libelle, Sens, ModeCalcul, MontantFixe, TauxSalarial, TauxPatronal, CodeBase,
                     SoumisIUTS, SoumisCNSS, SoumisTPA, Plafonne, Plafond, PourcentagePlafond, Ordre, Active,
                     EstSysteme, CompteComptable, CompteContrepartie, Commentaire)
                    VALUES
                    (@Code, @Libelle, @Sens, @ModeCalcul, @MontantFixe, @TauxSalarial, @TauxPatronal, @CodeBase,
                     @SoumisIUTS, @SoumisCNSS, @SoumisTPA, @Plafonne, @Plafond, @PourcentagePlafond, @Ordre, @Active,
                     @EstSysteme, @CompteComptable, @CompteContrepartie, @Commentaire);";

                cmd.Parameters.AjouterValeur("@Code", (r.Code ?? "").Trim().ToUpperInvariant());
                cmd.Parameters.AjouterValeur("@Libelle", r.Libelle ?? "");
                cmd.Parameters.AjouterValeur("@Sens", (int)r.Sens);
                cmd.Parameters.AjouterValeur("@ModeCalcul", (int)r.ModeCalcul);
                cmd.Parameters.AjouterValeur("@MontantFixe", DbValue.MoneyToDb(r.MontantFixe));
                cmd.Parameters.AjouterValeur("@TauxSalarial", DbValue.DecimalToDb(r.TauxSalarial));
                cmd.Parameters.AjouterValeur("@TauxPatronal", DbValue.DecimalToDb(r.TauxPatronal));
                cmd.Parameters.AjouterValeur("@CodeBase", r.CodeBase ?? "");
                cmd.Parameters.AjouterValeur("@SoumisIUTS", r.SoumisIUTS ? 1 : 0);
                cmd.Parameters.AjouterValeur("@SoumisCNSS", r.SoumisCNSS ? 1 : 0);
                cmd.Parameters.AjouterValeur("@SoumisTPA", r.SoumisTPA ? 1 : 0);
                cmd.Parameters.AjouterValeur("@Plafonne", r.Plafonne ? 1 : 0);
                cmd.Parameters.AjouterValeur("@Plafond", DbValue.MoneyToDb(r.Plafond));
                cmd.Parameters.AjouterValeur("@PourcentagePlafond", DbValue.DecimalToDb(r.PourcentagePlafond));
                cmd.Parameters.AjouterValeur("@Ordre", r.Ordre);
                cmd.Parameters.AjouterValeur("@Active", r.Active ? 1 : 0);
                cmd.Parameters.AjouterValeur("@EstSysteme", r.EstSysteme ? 1 : 0);
                cmd.Parameters.AjouterValeur("@CompteComptable", r.CompteComptable ?? "");
                cmd.Parameters.AjouterValeur("@CompteContrepartie", r.CompteContrepartie ?? "");
                cmd.Parameters.AjouterValeur("@Commentaire", r.Commentaire ?? "");

                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Supprime une rubrique. Une rubrique système n'est jamais supprimée : le moteur
        /// de calcul s'y réfère par son code. On la désactive.
        /// </summary>
        public async Task SupprimerAsync(Rubrique rubrique)
        {
            if (rubrique == null) return;

            if (rubrique.EstSysteme)
            {
                rubrique.Active = false;
                await EnregistrerAsync(rubrique);
                return;
            }

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM Rubriques WHERE Code = @Code";
                cmd.Parameters.AjouterValeur("@Code", rubrique.Code ?? "");
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<bool> CodeExisteAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            using (var conn = _sqliteService.CreerConnexion())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Rubriques WHERE Code = @Code";
                cmd.Parameters.AjouterValeur("@Code", code.Trim().ToUpperInvariant());
                var resultat = await cmd.ExecuteScalarAsync();
                return DbValue.IntFromDb(resultat) > 0;
            }
        }

        /// <summary>
        /// Plan de paie livré par défaut. Les plafonds reprennent exactement ceux que le
        /// moteur applique déjà (Art.106 du CGI) : 20 % du brut plafonnés à 75 000 pour le
        /// logement, 5 % plafonnés à 30 000 pour le transport, 5 % plafonnés à 50 000 pour
        /// la fonction. Les comptes SYSCOHADA sont proposés à titre de point de départ et
        /// se corrigent depuis l'écran.
        /// </summary>
        public static List<Rubrique> PlanDePaieInitial()
        {
            var plan = new List<Rubrique>
            {
                new Rubrique
                {
                    Code = CodeSalaireBase, Libelle = "Salaire de base",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Ordre = 10, Active = true, EstSysteme = true,
                    CompteComptable = "6611", CompteContrepartie = "4221",
                    Commentaire = "Salaire catégoriel, saisi sur la fiche du salarié."
                },
                new Rubrique
                {
                    Code = CodeIndemniteLogement, Libelle = "Indemnité de logement",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Plafonne = true, PourcentagePlafond = 0.20m, Plafond = 75000m,
                    Ordre = 20, Active = true, EstSysteme = true,
                    CompteComptable = "6631", CompteContrepartie = "4221",
                    Commentaire = "Exonérée dans la limite de l'article 106 du CGI ; l'excédent est réintégré."
                },
                new Rubrique
                {
                    Code = CodeIndemniteTransport, Libelle = "Indemnité de transport",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Plafonne = true, PourcentagePlafond = 0.05m, Plafond = 30000m,
                    Ordre = 30, Active = true, EstSysteme = true,
                    CompteComptable = "6632", CompteContrepartie = "4221",
                    Commentaire = "Exonérée dans la limite de l'article 106 du CGI ; l'excédent est réintégré."
                },
                new Rubrique
                {
                    Code = CodeIndemniteFonction, Libelle = "Indemnité de fonction",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Plafonne = true, PourcentagePlafond = 0.05m, Plafond = 50000m,
                    Ordre = 40, Active = true, EstSysteme = true,
                    CompteComptable = "6633", CompteContrepartie = "4221",
                    Commentaire = "Exonérée dans la limite de l'article 106 du CGI ; l'excédent est réintégré."
                },
                new Rubrique
                {
                    Code = CodeIndemniteSujetion, Libelle = "Indemnité de sujétion",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Ordre = 50, Active = true, EstSysteme = true,
                    CompteComptable = "6634", CompteContrepartie = "4221",
                    Commentaire = "Entre intégralement dans le salaire brut imposable."
                },
                new Rubrique
                {
                    Code = CodeCnssSalarie, Libelle = "CNSS part salariale",
                    Sens = SensRubrique.Retenue, ModeCalcul = ModeCalculRubrique.Moteur,
                    TauxSalarial = 0.055m, CodeBase = "",
                    Plafonne = true, Plafond = 800000m,
                    Ordre = 100, Active = true, EstSysteme = true,
                    CompteComptable = "4221", CompteContrepartie = "4311",
                    Commentaire = "5,5 % du salaire brut, assiette plafonnée à 800 000 FCFA par mois."
                },
                new Rubrique
                {
                    Code = CodeIuts, Libelle = "IUTS",
                    Sens = SensRubrique.Retenue, ModeCalcul = ModeCalculRubrique.Moteur,
                    Ordre = 110, Active = true, EstSysteme = true,
                    CompteComptable = "4221", CompteContrepartie = "4471",
                    Commentaire = "Calculé par le barème du CGI, après abattement et réduction pour charges familiales."
                }
            };

            plan.AddRange(RubriquesDeSaisie());
            return plan;
        }

        /// <summary>
        /// Rubriques destinées à la SAISIE MENSUELLE (module PAI-02).
        ///
        /// Sans elles, l'écran « Variables du mois » n'offre que le salaire de base et les
        /// quatre indemnités — c'est-à-dire de la rémunération permanente, qui se saisit
        /// sur la fiche du salarié et n'a rien à faire dans les variables d'un mois.
        ///
        /// Aucune n'est marquée « système » : renommez-les, changez leurs comptes,
        /// supprimez celles qui ne vous servent pas. Ce sont des points de départ, pas des
        /// contraintes.
        ///
        /// Les DRAPEAUX FISCAUX sont l'essentiel ici. Les trois familles couvrent les trois
        /// traitements que le moteur sait distinguer :
        ///
        ///   • soumis à l'IUTS ET à la CNSS — le cas ordinaire d'un complément de salaire ;
        ///   • soumis à l'IUTS SEULEMENT — l'indemnité de préavis, qui est un revenu
        ///     imposable mais ne correspond à aucune période travaillée cotisable ;
        ///   • soumis à NI L'UN NI L'AUTRE — un remboursement de frais n'est pas un revenu,
        ///     il rejoint le net sans passer par le calcul.
        ///
        /// Les RETENUES listées se déduisent du net APRÈS impôt et cotisations. Vous n'y
        /// trouverez pas de retenue pour absence : une absence diminue le salaire brut, et
        /// donc l'impôt et les cotisations — elle ne se modélise pas comme une retenue sur
        /// le net. C'est l'objet du module « Congés et absences », qui vient ensuite.
        ///
        /// ATTENTION : cette liste a été reprise à l'identique par la migration 11 de
        /// <see cref="SQLiteService"/>, au moment où ce module est arrivé — elle ne s'exécute
        /// plus pour les dossiers déjà passés par elle. Une rubrique ajoutée ICI après coup
        /// (comme <c>AVANCE</c>, migration 27) doit donc recevoir sa PROPRE migration
        /// <c>INSERT OR IGNORE</c>, pas une modification de la migration 11 : les dossiers
        /// déjà migrés ne la rejoueraient jamais.
        /// </summary>
        public static List<Rubrique> RubriquesDeSaisie()
        {
            return new List<Rubrique>
            {
                // ── Gains soumis à l'IUTS et à la CNSS ───────────────────────
                new Rubrique
                {
                    Code = "HSUP", Libelle = "Heures supplémentaires",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Ordre = 60, Active = true, EstSysteme = false,
                    CompteComptable = "6612", CompteContrepartie = "4221",
                    Commentaire = "Saisir le nombre d'heures en quantité et leur valeur en montant. " +
                                  "La valorisation automatique des heures relève du module PAI-03."
                },
                new Rubrique
                {
                    Code = "PANIER", Libelle = "Prime de panier",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Ordre = 62, Active = true, EstSysteme = false,
                    CompteComptable = "6612", CompteContrepartie = "4221",
                    Commentaire = "Nombre de paniers (postes travaillés) en quantité. " +
                                  "Alimentée par la Gestion des temps (RH-08)."
                },
                new Rubrique
                {
                    Code = "PRIME", Libelle = "Prime exceptionnelle",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Ordre = 61, Active = true, EstSysteme = false,
                    CompteComptable = "6617", CompteContrepartie = "4221",
                    Commentaire = "Prime ponctuelle, entièrement soumise à l'impôt et aux cotisations."
                },
                new Rubrique
                {
                    Code = "PRIMEREND", Libelle = "Prime de rendement",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Ordre = 62, Active = true, EstSysteme = false,
                    CompteComptable = "6617", CompteContrepartie = "4221",
                    Commentaire = "Prime liée aux résultats, variable d'un mois à l'autre."
                },
                new Rubrique
                {
                    Code = "PRIMEANC", Libelle = "Prime d'ancienneté",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Ordre = 63, Active = true, EstSysteme = false,
                    CompteComptable = "6613", CompteContrepartie = "4221",
                    Commentaire = "Le calcul par tranche d'ancienneté relève du module SOC-05 (grilles et conventions)."
                },
                new Rubrique
                {
                    Code = "GRATIF", Libelle = "Gratification / 13e mois",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true,
                    Ordre = 64, Active = true, EstSysteme = false,
                    CompteComptable = "6618", CompteContrepartie = "4221",
                    Commentaire = "Le prorata selon le temps de présence relève du module PAI-10."
                },

                // ── Gain soumis à l'IUTS seulement ───────────────────────────
                new Rubrique
                {
                    Code = "INDPREAVIS", Libelle = "Indemnité compensatrice de préavis",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = false,
                    Ordre = 70, Active = true, EstSysteme = false,
                    CompteComptable = "6635", CompteContrepartie = "4221",
                    Commentaire = "Revenu imposable, mais hors assiette CNSS : elle ne correspond à aucune période travaillée."
                },

                // ── Rubriques du bulletin de sortie (solde de tout compte) ───
                new Rubrique
                {
                    Code = "INDCONGES", Libelle = "Indemnité compensatrice de congés payés",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true, SoumisTPA = true,
                    Ordre = 71, Active = true, EstSysteme = false,
                    CompteComptable = "6612", CompteContrepartie = "4221",
                    Commentaire = "Congés acquis et non pris à la rupture : un salaire, imposable et cotisable " +
                                  "(instruction DGI sur l'imposition des sommes perçues suite à un licenciement)."
                },
                new Rubrique
                {
                    Code = "INDDEPART", Libelle = "Indemnité de licenciement / de départ",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = false, SoumisCNSS = false, SoumisTPA = false,
                    Ordre = 72, Active = true, EstSysteme = false,
                    CompteComptable = "6638", CompteContrepartie = "4221",
                    Commentaire = "Indemnité de licenciement, de fin de service ou de départ à la retraite (CCI-BF art. 35 et 38). " +
                                  "Exonérée d'IUTS (art. 106-5 du CGI) ; ce n'est pas un salaire : hors CNSS et hors TPA."
                },
                new Rubrique
                {
                    Code = "INDDIVERS", Libelle = "Indemnités diverses de sortie",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = true, SoumisCNSS = true, SoumisTPA = true,
                    Ordre = 73, Active = true, EstSysteme = false,
                    CompteComptable = "6638", CompteContrepartie = "4221",
                    Commentaire = "Sommes diverses versées au départ. Des dommages-intérêts pour rupture abusive sont " +
                                  "exonérés d'IUTS : décochez alors les cases concernées."
                },
                new Rubrique
                {
                    Code = "RETSORTIE", Libelle = "Retenue de sortie",
                    Sens = SensRubrique.Retenue, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    Ordre = 125, Active = true, EstSysteme = false,
                    CompteComptable = "4221", CompteContrepartie = "4281",
                    Commentaire = "Déduction diverse du solde de tout compte (matériel non restitué, trop-perçu…)."
                },

                // ── Gains hors impôt et hors cotisations ─────────────────────
                new Rubrique
                {
                    Code = "REMBFRAIS", Libelle = "Remboursement de frais",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    // Ni salaire ni avantage : hors assiette de la TPA comme de l'IUTS et de la CNSS.
                    SoumisIUTS = false, SoumisCNSS = false, SoumisTPA = false,
                    Ordre = 75, Active = true, EstSysteme = false,
                    CompteComptable = "6181", CompteContrepartie = "4221",
                    Commentaire = "Remboursement d'une dépense avancée par le salarié : ce n'est pas un revenu. " +
                                  "Rejoint le net sans passer par le calcul. À justifier par la pièce."
                },
                new Rubrique
                {
                    Code = "FRAISMISS", Libelle = "Frais de mission",
                    Sens = SensRubrique.Gain, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    SoumisIUTS = false, SoumisCNSS = false, SoumisTPA = false,
                    Ordre = 76, Active = true, EstSysteme = false,
                    CompteComptable = "6185", CompteContrepartie = "4221",
                    Commentaire = "Indemnité de déplacement couvrant une dépense réelle. Une indemnité forfaitaire " +
                                  "excédant la dépense engagée redevient imposable : cochez alors « soumis à l'IUTS »."
                },

                // ── Retenues sur le net à payer ──────────────────────────────
                new Rubrique
                {
                    Code = "AVANCE", Libelle = "Avance sur salaire",
                    Sens = SensRubrique.Retenue, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    Ordre = 119, Active = true, EstSysteme = false,
                    CompteComptable = "4221", CompteContrepartie = "4270",
                    Commentaire = "Somme versée avant l'échéance, récupérée sur le net. L'échéancier automatique " +
                                  "relève du module PAI-05 (type « Avance sur salaire », distinct de l'acompte)."
                },
                new Rubrique
                {
                    Code = "ACOMPTE", Libelle = "Acompte sur salaire",
                    Sens = SensRubrique.Retenue, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    Ordre = 120, Active = true, EstSysteme = false,
                    CompteComptable = "4221", CompteContrepartie = "4271",
                    Commentaire = "Avance déjà versée, récupérée sur le net. Ne diminue ni l'impôt ni les cotisations."
                },
                new Rubrique
                {
                    Code = "PRET", Libelle = "Remboursement de prêt",
                    Sens = SensRubrique.Retenue, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    Ordre = 121, Active = true, EstSysteme = false,
                    CompteComptable = "4221", CompteContrepartie = "4272",
                    Commentaire = "Échéance de prêt au personnel. L'échéancier automatique relève du module PAI-05."
                },
                new Rubrique
                {
                    Code = "OPPOSITION", Libelle = "Saisie-arrêt / opposition",
                    Sens = SensRubrique.Retenue, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    Ordre = 122, Active = true, EstSysteme = false,
                    CompteComptable = "4221", CompteContrepartie = "4273",
                    Commentaire = "Retenue judiciaire ou pension alimentaire. Le contrôle de la quotité saisissable " +
                                  "relève du module PAI-06 : vérifiez-la vous-même en attendant."
                },
                new Rubrique
                {
                    Code = "RETSYND", Libelle = "Cotisation syndicale",
                    Sens = SensRubrique.Retenue, ModeCalcul = ModeCalculRubrique.MontantFixe,
                    Ordre = 123, Active = true, EstSysteme = false,
                    CompteComptable = "4221", CompteContrepartie = "4274",
                    Commentaire = "Précomptée sur le net, reversée au syndicat."
                }
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Construit le tableau de bord masse salariale (module PIL-01).
    ///
    /// Même principe que <see cref="LivrePaieService"/> et <see cref="EtatAnnuelSalairesService"/> :
    /// le service ne recalcule rien, il lit les bulletins déjà enregistrés sur les mois
    /// clôturés de l'année demandée et les agrège selon trois axes — le mois (évolution),
    /// la catégorie et le service du salarié.
    ///
    /// « Coût employeur » est ici la colonne <c>MasseSalariale</c> de TaxCalculations —
    /// brut plus charges patronales — exactement la même grandeur que
    /// <see cref="PeriodePaie.CoutTotalEmployeur"/> / <see cref="PeriodePaie.TotalMasseSalariale"/>,
    /// simplement nommée d'après ce que l'écran affiche.
    /// </summary>
    public class TableauBordMasseSalarialeService
    {
        /// <summary>Hauteur maximale, en pixels, d'une barre du graphique d'évolution.</summary>
        private const double HauteurMaxGraphiquePx = 130.0;

        /// <summary>Largeur maximale, en pixels, d'une barre de ventilation.</summary>
        private const double LargeurMaxVentilationPx = 220.0;

        /// <summary>Hauteur/largeur plancher pour qu'une valeur non nulle reste visible.</summary>
        private const double DimensionMinimalePx = 2.0;

        private readonly SQLiteService _sqliteService;
        private readonly PeriodePaieService _periodeService;

        public TableauBordMasseSalarialeService(SQLiteService sqliteService, PeriodePaieService periodeService)
        {
            if (sqliteService == null) throw new ArgumentNullException("sqliteService");
            if (periodeService == null) throw new ArgumentNullException("periodeService");

            _sqliteService = sqliteService;
            _periodeService = periodeService;
        }

        public async Task<TableauBordMasseSalariale> ConstruireAsync(Dossier dossier, int annee)
        {
            if (dossier == null) throw new ArgumentNullException("dossier");

            var tableau = new TableauBordMasseSalariale
            {
                RaisonSociale = dossier.RaisonSociale,
                Ifu = dossier.IFU,
                Rccm = dossier.RCCM,
                Adresse = dossier.Adresse,
                Ville = dossier.Ville,
                Telephone = dossier.Telephone,
                Annee = annee
            };

            var moisFermes = new HashSet<int>();
            for (int mois = 1; mois <= 12; mois++)
            {
                var periode = await _periodeService.LirePeriodeAsync(annee, mois);
                if (periode != null && periode.EstCloturee && periode.NombreBulletins > 0)
                {
                    moisFermes.Add(mois);
                    tableau.MoisClotures.Add(mois);
                }
                else
                {
                    tableau.MoisManquants.Add(mois);
                }
            }

            if (moisFermes.Count == 0)
                return tableau;

            await RemplirAsync(tableau, moisFermes);
            return tableau;
        }

        private async Task RemplirAsync(TableauBordMasseSalariale tableau, HashSet<int> moisFermes)
        {
            var debut = new DateTime(tableau.Annee, 1, 1);
            var fin = new DateTime(tableau.Annee, 12, 31, 23, 59, 59);

            var parMois = new Dictionary<int, PointEvolutionMensuelle>();
            var parCategorie = new Dictionary<string, VentilationMasseSalariale>(StringComparer.OrdinalIgnoreCase);
            var parService = new Dictionary<string, VentilationMasseSalariale>(StringComparer.OrdinalIgnoreCase);

            using (var conn = _sqliteService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT c.CalculationDate,
                           c.SalaireBrut,
                           c.SalaireNetaPayer,
                           c.MasseSalariale,
                           e.Category,
                           e.Service
                    FROM TaxCalculations c
                    LEFT JOIN Employees e ON e.EmployeeId = c.EmployeeId
                    WHERE c.CalculationDate >= @debut
                      AND c.CalculationDate <= @fin
                      AND c.IsAnnual = 0
                      AND c.IsRegularization = 0";

                cmd.Parameters.AddWithValue("@debut", DbValue.DateToDb(debut));
                cmd.Parameters.AddWithValue("@fin", DbValue.DateToDb(fin));

                using (var reader = (SQLiteDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        DateTime dateCalcul = DbValue.DateFromDb(reader["CalculationDate"], default(DateTime));
                        if (dateCalcul == default(DateTime) || !moisFermes.Contains(dateCalcul.Month))
                            continue;

                        decimal brut = DbValue.MoneyFromDb(reader["SalaireBrut"]);
                        decimal net = DbValue.MoneyFromDb(reader["SalaireNetaPayer"]);
                        decimal coutEmployeur = DbValue.MoneyFromDb(reader["MasseSalariale"]);

                        // ── Évolution mensuelle ──────────────────────────────────
                        PointEvolutionMensuelle point;
                        if (!parMois.TryGetValue(dateCalcul.Month, out point))
                        {
                            point = new PointEvolutionMensuelle
                            {
                                Mois = dateCalcul.Month,
                                Designation = NomMois(dateCalcul.Month)
                            };
                            parMois[dateCalcul.Month] = point;
                        }
                        point.Brut += brut;
                        point.Net += net;
                        point.CoutEmployeur += coutEmployeur;
                        point.NombreBulletins++;

                        // ── Ventilation par catégorie ────────────────────────────
                        string categorie = DbValue.TextFromDb(reader["Category"]);
                        AccumulerVentilation(parCategorie, categorie, brut, net, coutEmployeur);

                        // ── Ventilation par service ──────────────────────────────
                        string service = DbValue.TextFromDb(reader["Service"]);
                        AccumulerVentilation(parService, service, brut, net, coutEmployeur);
                    }
                }
            }

            tableau.Evolution = parMois.Values.OrderBy(p => p.Mois).ToList();
            tableau.ParCategorie = parCategorie.Values.OrderByDescending(v => v.CoutEmployeur).ToList();
            tableau.ParService = parService.Values.OrderByDescending(v => v.CoutEmployeur).ToList();

            PrecalculerDimensions(tableau);
        }

        private static void AccumulerVentilation(
            Dictionary<string, VentilationMasseSalariale> table, string cle,
            decimal brut, decimal net, decimal coutEmployeur)
        {
            string cleEffective = string.IsNullOrWhiteSpace(cle) ? "Non renseigné" : cle.Trim();

            VentilationMasseSalariale ligne;
            if (!table.TryGetValue(cleEffective, out ligne))
            {
                ligne = new VentilationMasseSalariale { Cle = cleEffective };
                table[cleEffective] = ligne;
            }

            ligne.Brut += brut;
            ligne.Net += net;
            ligne.CoutEmployeur += coutEmployeur;
            ligne.NombreBulletins++;
        }

        /// <summary>
        /// Calcule les hauteurs et largeurs de barres en pixels, une fois tous les totaux
        /// connus. Fait ici — et non dans la vue — pour rester cohérent avec le reste du
        /// projet, où l'affichage lit des propriétés déjà prêtes plutôt que de recalculer
        /// une échelle dans un convertisseur.
        /// </summary>
        private static void PrecalculerDimensions(TableauBordMasseSalariale tableau)
        {
            decimal maxMensuel = tableau.Evolution.Count == 0
                ? 0m
                : tableau.Evolution.Max(p => Math.Max(p.CoutEmployeur, Math.Max(p.Brut, p.Net)));

            foreach (var p in tableau.Evolution)
            {
                p.HauteurBrutPx = Echelle(p.Brut, maxMensuel);
                p.HauteurNetPx = Echelle(p.Net, maxMensuel);
                p.HauteurCoutEmployeurPx = Echelle(p.CoutEmployeur, maxMensuel);
            }

            AppliquerLargeurs(tableau.ParCategorie);
            AppliquerLargeurs(tableau.ParService);
        }

        private static void AppliquerLargeurs(List<VentilationMasseSalariale> lignes)
        {
            if (lignes.Count == 0) return;

            decimal totalCoutEmployeur = lignes.Sum(v => v.CoutEmployeur);
            decimal maxCoutEmployeur = lignes.Max(v => v.CoutEmployeur);

            foreach (var v in lignes)
            {
                v.PartPourcentage = totalCoutEmployeur == 0m
                    ? 0.0
                    : (double)(v.CoutEmployeur / totalCoutEmployeur) * 100.0;
                v.LargeurBarrePx = Echelle(v.CoutEmployeur, maxCoutEmployeur, LargeurMaxVentilationPx);
            }
        }

        private static double Echelle(decimal valeur, decimal maximum, double dimensionMax = HauteurMaxGraphiquePx)
        {
            if (valeur <= 0m || maximum <= 0m) return 0.0;

            double ratio = (double)(valeur / maximum);
            double dimension = ratio * dimensionMax;
            return dimension < DimensionMinimalePx ? DimensionMinimalePx : dimension;
        }

        private static string NomMois(int mois)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            string nom = culture.DateTimeFormat.GetMonthName(mois);
            return nom.Length > 0 ? char.ToUpper(nom[0], culture) + nom.Substring(1) : nom;
        }
    }
}

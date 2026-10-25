using System;
using System.Collections.Generic;
using System.Linq;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Moteur du relevé des temps (module RH-08). Aucune dépendance à la base : tout ce
    /// qu'il calcule se vérifie par des tests unitaires, à partir d'un planning en mémoire.
    ///
    /// <b>Règles de décompte</b>, celles de l'état de salaire de rotation (onglet « 01 »),
    /// corrigées de ses deux défauts :
    ///   • heures d'un jour : durée du poste de l'équipe (J/FJ : poste de jour, N/FN : poste
    ///     de nuit), ou les heures pointées quand elles sont connues ; R, A, CP, M : 0 ;
    ///     AJ : 0, ou les heures normales d'un poste si le dossier le décide ;
    ///   • un J ou un N tombant un jour férié (ou un dimanche, si l'équipe le majore) est
    ///     traité comme FJ ou FN ;
    ///   • par semaine : normales = min(durée légale, total) ; HS = le reste ; puis, dans
    ///     cet ordre, HS de nuit = heures des nuits au-delà des heures normales d'un poste,
    ///     tranche 1 = les 8 premières HS restantes, férié de jour, férié de nuit, et la
    ///     tranche 2 reçoit le solde. Chaque tranche ne prend que ce qui reste après les
    ///     précédentes : la somme des tranches est TOUJOURS égale aux HS de la semaine
    ///     (le classeur pouvait compter deux fois les mêmes heures quand une semaine
    ///     mêlait nuits et fériés) ;
    ///   • le poste de jour férié se mesure sur la durée du poste de JOUR (le classeur
    ///     prenait celle de nuit).
    ///
    /// <b>Semaines à cheval sur deux périodes.</b> Une semaine est rattachée à la période
    /// qui contient son DERNIER jour : elle est décomptée en entier, une seule fois, y
    /// compris ses jours de la période précédente. Les derniers jours d'une période dont
    /// la semaine se termine après l'arrêté sont décomptés avec la période suivante.
    /// </summary>
    public static class CalculTemps
    {
        /// <summary>Premier jour de la semaine qui contient <paramref name="date"/>.</summary>
        public static DateTime DebutSemaine(DateTime date, DayOfWeek premierJour)
        {
            int ecart = ((int)date.DayOfWeek - (int)premierJour + 7) % 7;
            return date.Date.AddDays(-ecart);
        }

        /// <summary>Premier jour à charger pour décompter une période (début de sa première semaine).</summary>
        public static DateTime DebutChargement(PeriodePointage periode, DayOfWeek premierJour)
        {
            return DebutSemaine(periode.Debut, premierJour);
        }

        /// <summary>Semaines rattachées à la période : celles dont le dernier jour tombe dans la période.</summary>
        public static List<DateTime> SemainesDeLaPeriode(PeriodePointage periode, DayOfWeek premierJour)
        {
            var debuts = new List<DateTime>();
            for (var d = DebutSemaine(periode.Debut, premierJour); d.AddDays(6) <= periode.Fin; d = d.AddDays(7))
            {
                if (d.AddDays(6) >= periode.Debut)
                    debuts.Add(d);
            }
            return debuts;
        }

        /// <summary>Code effectif d'un jour : J/N devient FJ/FN un jour férié (ou un dimanche majoré).</summary>
        public static string CodeEffectif(string code, DateTime date, ISet<DateTime> feries, EquipeTravail equipe)
        {
            if (code != CodesPlanning.Jour && code != CodesPlanning.Nuit) return code ?? string.Empty;

            bool ferie = (feries != null && feries.Contains(date.Date))
                         || (equipe != null && equipe.DimancheMajore && date.DayOfWeek == DayOfWeek.Sunday);
            if (!ferie) return code;

            return code == CodesPlanning.Nuit ? CodesPlanning.FerieNuit : CodesPlanning.FerieJour;
        }

        /// <summary>Heures retenues pour un jour du planning.</summary>
        public static decimal HeuresDuJour(PlanningJour jour, string codeEffectif, EquipeTravail equipe, ParametresTemps parametres)
        {
            if (equipe == null) return jour != null && jour.Heures.HasValue ? Math.Max(0m, jour.Heures.Value) : 0m;

            if (CodesPlanning.EstTravaille(codeEffectif))
            {
                if (jour != null && jour.Heures.HasValue) return Math.Max(0m, jour.Heures.Value);
                return equipe.DureePoste(codeEffectif);
            }

            if (codeEffectif == CodesPlanning.AbsenceJustifiee && parametres != null && parametres.AJCompteDansSeuil)
            {
                if (jour != null && jour.Heures.HasValue) return Math.Max(0m, jour.Heures.Value);
                return equipe.HeuresNormalesParPoste;
            }

            return 0m;
        }

        /// <summary>
        /// Décompte d'une semaine. <paramref name="jours"/> : les 7 jours dans l'ordre, chacun
        /// avec son code effectif, ses heures et les heures normales d'un poste de son équipe.
        /// </summary>
        public static SemaineDecompte DecompterSemaine(
            DateTime debut, IList<(string Code, decimal Heures, decimal HeuresNormalesPoste)> jours, BaremeHeuresSup bareme)
        {
            bareme = bareme ?? BaremeHeuresSup.Historique();

            decimal total = 0m, excesNuit = 0m, excesFerieJour = 0m, excesFerieNuit = 0m;
            var codes = new List<string>();

            foreach (var j in jours)
            {
                total += j.Heures;
                decimal exces = Math.Max(0m, j.Heures - j.HeuresNormalesPoste);
                if (j.Code == CodesPlanning.Nuit) excesNuit += exces;
                else if (j.Code == CodesPlanning.FerieJour) excesFerieJour += exces;
                else if (j.Code == CodesPlanning.FerieNuit) excesFerieNuit += exces;
                codes.Add(string.IsNullOrEmpty(j.Code) ? "·" : j.Code);
            }

            decimal normales = Math.Min(bareme.DureeHebdoLegale, total);
            decimal hs = Math.Max(0m, total - normales);

            decimal nuit = Math.Min(hs, excesNuit);
            decimal tranche1 = Math.Min(bareme.SeuilTranche1, hs - nuit);
            decimal ferieJour = Math.Min(hs - nuit - tranche1, excesFerieJour);
            decimal ferieNuit = Math.Min(hs - nuit - tranche1 - ferieJour, excesFerieNuit);
            decimal tranche2 = hs - nuit - tranche1 - ferieJour - ferieNuit;

            return new SemaineDecompte
            {
                Debut = debut.Date,
                Fin = debut.Date.AddDays(6),
                HeuresTravaillees = total,
                HeuresNormales = normales,
                HeuresSup = hs,
                Tranche1 = tranche1,
                Tranche2 = tranche2,
                Nuit = nuit,
                FerieJour = ferieJour,
                FerieNuit = ferieNuit,
                Codes = string.Join(" ", codes)
            };
        }

        /// <summary>
        /// Récapitulatif d'un salarié sur une période de pointage.
        /// </summary>
        /// <param name="planning">Jours du salarié couvrant au moins [début de la première semaine, fin de période].</param>
        /// <param name="equipeActuelle">Équipe actuelle : semaine de décompte, et durées des jours sans équipe propre.</param>
        /// <param name="equipes">Équipes par identifiant, pour les jours rattachés à une autre équipe.</param>
        public static RecapTempsSalarie Decompter(
            Employee salarie,
            PeriodePointage periode,
            IEnumerable<PlanningJour> planning,
            EquipeTravail equipeActuelle,
            IDictionary<int, EquipeTravail> equipes,
            ISet<DateTime> feries,
            BaremeHeuresSup bareme,
            ParametresTemps parametres)
        {
            if (salarie == null) throw new ArgumentNullException(nameof(salarie));
            if (periode == null) throw new ArgumentNullException(nameof(periode));
            parametres = parametres ?? new ParametresTemps();
            bareme = bareme ?? BaremeHeuresSup.Historique();

            var recap = new RecapTempsSalarie
            {
                EmployeeId = salarie.EmployeeId,
                NomComplet = salarie.FullName,
                EquipeCode = equipeActuelle != null ? equipeActuelle.Code : string.Empty,
                SalaireDeBase = salarie.BaseSalary
            };

            var parDate = new Dictionary<DateTime, PlanningJour>();
            foreach (var j in planning ?? Enumerable.Empty<PlanningJour>())
            {
                if (j == null) continue;
                parDate[j.Date.Date] = j;
            }

            Func<PlanningJour, EquipeTravail> equipeDu = j =>
            {
                EquipeTravail e;
                if (j != null && j.EquipeId.HasValue && equipes != null && equipes.TryGetValue(j.EquipeId.Value, out e) && e != null)
                    return e;
                return equipeActuelle;
            };

            // ── Jours de la période ─────────────────────────────────────────
            for (var d = periode.Debut; d <= periode.Fin; d = d.AddDays(1))
            {
                PlanningJour jour;
                parDate.TryGetValue(d, out jour);
                string code = jour != null ? (CodesPlanning.Normaliser(jour.Code) ?? string.Empty) : string.Empty;
                var equipe = equipeDu(jour);
                string effectif = CodeEffectif(code, d, feries, equipe);

                switch (code)
                {
                    case "": recap.JoursNonSaisis++; break;
                    case CodesPlanning.Repos: recap.JoursRepos++; break;
                    case CodesPlanning.AbsenceJustifiee: recap.JoursAbsenceJustifiee++; break;
                    case CodesPlanning.Conge: recap.JoursConge++; break;
                    case CodesPlanning.Maladie: recap.JoursMaladie++; break;
                    case CodesPlanning.AbsenceNonJustifiee:
                        recap.JoursAbsenceNonJustifiee++;
                        recap.AbsencesNonJustifiees.Add(new PlanningJour
                        {
                            EmployeeId = salarie.EmployeeId,
                            Date = d,
                            Code = code,
                            Heures = jour != null && jour.Heures.HasValue
                                ? jour.Heures
                                : (equipe != null ? equipe.DureePosteJour : (decimal?)null),
                            EquipeId = jour != null ? jour.EquipeId : null
                        });
                        break;
                }

                if (CodesPlanning.EstTravaille(effectif))
                {
                    recap.JoursTravailles++;
                    recap.Paniers++;
                }

                recap.HeuresPeriode += HeuresDuJour(jour, effectif, equipe, parametres);
            }

            // ── Semaines rattachées à la période ───────────────────────────
            var premierJour = equipeActuelle != null ? equipeActuelle.PremierJourSemaine : DayOfWeek.Monday;
            foreach (var debut in SemainesDeLaPeriode(periode, premierJour))
            {
                var jours = new List<(string Code, decimal Heures, decimal HeuresNormalesPoste)>();
                for (int i = 0; i < 7; i++)
                {
                    var d = debut.AddDays(i);
                    PlanningJour jour;
                    parDate.TryGetValue(d, out jour);
                    string code = jour != null ? (CodesPlanning.Normaliser(jour.Code) ?? string.Empty) : string.Empty;
                    var equipe = equipeDu(jour);
                    string effectif = CodeEffectif(code, d, feries, equipe);
                    decimal heures = HeuresDuJour(jour, effectif, equipe, parametres);
                    decimal normalesPoste = equipe != null ? equipe.HeuresNormalesParPoste : 8m;
                    jours.Add((effectif, heures, normalesPoste));
                }

                var semaine = DecompterSemaine(debut, jours, bareme);
                recap.Semaines.Add(semaine);

                recap.HeuresDecomptees += semaine.HeuresTravaillees;
                recap.HeuresNormales += semaine.HeuresNormales;
                recap.HeuresSup += semaine.HeuresSup;
                recap.Tranche1 += semaine.Tranche1;
                recap.Tranche2 += semaine.Tranche2;
                recap.Nuit += semaine.Nuit;
                recap.FerieJour += semaine.FerieJour;
                recap.FerieNuit += semaine.FerieNuit;
            }

            if (recap.Semaines.Count > 0)
            {
                recap.DebutDecompte = recap.Semaines.First().Debut;
                recap.FinDecompte = recap.Semaines.Last().Fin;
            }

            // ── Paniers ────────────────────────────────────────────────────
            recap.PanierActif = equipeActuelle != null && equipeActuelle.PanierMode != ModePanier.Aucun;
            if (equipeActuelle != null && recap.Paniers > 0)
            {
                switch (equipeActuelle.PanierMode)
                {
                    case ModePanier.MontantFixe:
                        recap.MontantPanier = Math.Round(recap.Paniers * equipeActuelle.PanierValeur, 0);
                        break;
                    case ModePanier.FractionTauxHoraire:
                        decimal tauxHoraire = salarie.BaseSalary > 0m
                            ? Math.Round(salarie.BaseSalary / HeureSupplementaire.HeuresMensuellesReference, 2)
                            : 0m;
                        recap.MontantPanier = Math.Round(recap.Paniers * tauxHoraire * equipeActuelle.PanierValeur, 0);
                        break;
                }
            }

            // ── Avertissements ─────────────────────────────────────────────
            if (equipeActuelle == null)
                recap.Avertissements.Add("Salarié affecté à aucune équipe.");
            if (recap.JoursNonSaisis > 0)
                recap.Avertissements.Add(string.Format("{0} jour(s) non saisi(s).", recap.JoursNonSaisis));
            if (recap.FinDecompte.HasValue && recap.FinDecompte.Value < periode.Fin)
            {
                recap.Avertissements.Add(string.Format(
                    "Jours du {0:dd/MM} au {1:dd/MM} décomptés avec la période suivante (semaine à cheval).",
                    recap.FinDecompte.Value.AddDays(1), periode.Fin));
            }

            return recap;
        }

        /// <summary>
        /// Regroupe les jours « A » consécutifs en absences non rémunérées : une absence par
        /// série de jours qui se suivent, avec le nombre de jours et les heures de postes manqués.
        /// </summary>
        public static List<Absence> AbsencesDepuisReleve(RecapTempsSalarie recap, PeriodePointage periode)
        {
            var absences = new List<Absence>();
            if (recap == null) return absences;

            var jours = recap.AbsencesNonJustifiees.OrderBy(j => j.Date).ToList();
            int i = 0;
            while (i < jours.Count)
            {
                int k = i;
                while (k + 1 < jours.Count && jours[k + 1].Date == jours[k].Date.AddDays(1)) k++;

                var serie = jours.GetRange(i, k - i + 1);
                absences.Add(new Absence
                {
                    EmployeeId = recap.EmployeeId,
                    NomSalarie = recap.NomComplet,
                    Type = TypeAbsence.AbsenceNonRemuneree,
                    DateDebut = serie.First().Date,
                    DateFin = serie.Last().Date,
                    NombreJours = serie.Count,
                    NombreHeures = serie.Sum(j => j.Heures ?? 0m),
                    Commentaire = string.Format("{0} Absence non justifiée relevée au planning ({1})",
                        AbsenceService.MarqueurReleveTemps,
                        periode != null ? "période " + periode.Libelle : "relevé des temps")
                });

                i = k + 1;
            }

            return absences;
        }
    }
}

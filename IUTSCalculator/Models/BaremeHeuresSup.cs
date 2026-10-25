using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Taux de majoration des heures supplémentaires, valables à partir d'une DATE D'EFFET
    /// (modules PAI-03 et RH-08).
    ///
    /// Le module PAI-03 appliquait jusqu'ici quatre majorations écrites en dur dans le
    /// modèle (15 %, 30 %, 50 %, 100 %). Le relevé des rotations (RH-08) en distingue
    /// cinq, comme le font les bulletins des entreprises minières du Burkina :
    ///   • tranche 1 — les 8 premières heures supplémentaires de la semaine (41e à 48e) ;
    ///   • tranche 2 — les heures supplémentaires au-delà de la 48e heure ;
    ///   • nuit      — les heures supplémentaires de nuit d'un jour ouvrable ;
    ///   • férié jour / férié nuit — celles d'un dimanche ou d'un jour férié, de jour ou de nuit.
    ///
    /// Le nouveau Code du travail (loi n°013-2026/ALP du 6 mai 2026) renvoie ces taux aux
    /// conventions collectives et aux textes d'application : ils sont donc paramétrables,
    /// et datés comme les barèmes légaux (SOC-02). Un barème n'est jamais modifié quand
    /// la règle change : on en crée un nouveau avec une nouvelle date d'effet. Le barème
    /// applicable à un mois de paie est celui dont la date d'effet est la plus récente
    /// parmi celles antérieures ou égales au premier jour du mois.
    ///
    /// Tous les taux sont des PROPORTIONS (0,15 pour 15 %), stockés en TEXT invariant.
    /// </summary>
    public class BaremeHeuresSup : INotifyPropertyChanged
    {
        private int _id;
        private DateTime _dateEffet;
        private string _libelle;
        private string _reference;
        private bool _estSysteme;

        private decimal _tauxTranche1;
        private decimal _tauxTranche2;
        private decimal _tauxNuit;
        private decimal _tauxFerieJour;
        private decimal _tauxFerieNuit;
        private decimal _seuilTranche1;
        private decimal _dureeHebdoLegale;

        public int Id
        {
            get { return _id; }
            set { Definir(ref _id, value); }
        }

        /// <summary>Premier jour à partir duquel ces taux s'appliquent.</summary>
        public DateTime DateEffet
        {
            get { return _dateEffet; }
            set { if (Definir(ref _dateEffet, value.Date)) OnPropertyChanged(nameof(Designation)); }
        }

        public string Libelle
        {
            get { return _libelle; }
            set { if (Definir(ref _libelle, value)) OnPropertyChanged(nameof(Designation)); }
        }

        /// <summary>Texte d'origine : convention collective, décret, accord d'entreprise.</summary>
        public string Reference
        {
            get { return _reference; }
            set { Definir(ref _reference, value); }
        }

        /// <summary>Barème livré avec l'application : modifiable, mais jamais supprimable.</summary>
        public bool EstSysteme
        {
            get { return _estSysteme; }
            set { Definir(ref _estSysteme, value); }
        }

        /// <summary>Majoration des 8 premières heures supplémentaires de la semaine (0,15).</summary>
        public decimal TauxTranche1
        {
            get { return _tauxTranche1; }
            set { if (Definir(ref _tauxTranche1, value)) OnPropertyChanged(nameof(Resume)); }
        }

        /// <summary>Majoration des heures supplémentaires au-delà de la 48e heure (0,35).</summary>
        public decimal TauxTranche2
        {
            get { return _tauxTranche2; }
            set { if (Definir(ref _tauxTranche2, value)) OnPropertyChanged(nameof(Resume)); }
        }

        /// <summary>Majoration des heures supplémentaires de nuit d'un jour ouvrable (0,50).</summary>
        public decimal TauxNuit
        {
            get { return _tauxNuit; }
            set { if (Definir(ref _tauxNuit, value)) OnPropertyChanged(nameof(Resume)); }
        }

        /// <summary>Majoration des heures supplémentaires de jour, dimanche ou jour férié (0,60).</summary>
        public decimal TauxFerieJour
        {
            get { return _tauxFerieJour; }
            set { if (Definir(ref _tauxFerieJour, value)) OnPropertyChanged(nameof(Resume)); }
        }

        /// <summary>Majoration des heures supplémentaires de nuit, dimanche ou jour férié (1,20).</summary>
        public decimal TauxFerieNuit
        {
            get { return _tauxFerieNuit; }
            set { if (Definir(ref _tauxFerieNuit, value)) OnPropertyChanged(nameof(Resume)); }
        }

        /// <summary>Nombre d'heures supplémentaires de la semaine majorées au taux de la tranche 1 (8).</summary>
        public decimal SeuilTranche1
        {
            get { return _seuilTranche1; }
            set { Definir(ref _seuilTranche1, value); }
        }

        /// <summary>Durée légale hebdomadaire au-delà de laquelle une heure est supplémentaire (40).</summary>
        public decimal DureeHebdoLegale
        {
            get { return _dureeHebdoLegale; }
            set { Definir(ref _dureeHebdoLegale, value); }
        }

        // ── Libellés ─────────────────────────────────────────────────────────

        public string Designation
        {
            get
            {
                string libelle = string.IsNullOrWhiteSpace(Libelle) ? "Barème HS" : Libelle;
                return string.Format("{0} — à partir du {1:dd/MM/yyyy}", libelle, DateEffet);
            }
        }

        /// <summary>Les cinq taux en une ligne : « 15 % · 35 % · 50 % · 60 % · 120 % ».</summary>
        public string Resume
        {
            get
            {
                return string.Format("{0} · {1} · {2} · {3} · {4}",
                    Pourcent(TauxTranche1), Pourcent(TauxTranche2), Pourcent(TauxNuit),
                    Pourcent(TauxFerieJour), Pourcent(TauxFerieNuit));
            }
        }

        public static string Pourcent(decimal taux)
        {
            return string.Format("{0:0.##} %", taux * 100m);
        }

        /// <summary>Libellé de bulletin d'une majoration : 0,35 → « 135% ».</summary>
        public static string TauxBulletin(decimal taux)
        {
            return string.Format("{0:0.##}%", 100m + taux * 100m);
        }

        // ── Barèmes livrés ───────────────────────────────────────────────────

        /// <summary>
        /// Taux appliqués par le module PAI-03 avant ce barème. Le dimanche et le férié de
        /// jour étaient alors majorés comme la nuit (50 %) : on reproduit exactement ce
        /// calcul pour que les mois antérieurs restent inchangés au centime.
        /// </summary>
        public static BaremeHeuresSup Historique()
        {
            return new BaremeHeuresSup
            {
                DateEffet = new DateTime(2000, 1, 1),
                Libelle = "Taux initiaux du module PAI-03",
                Reference = "Valeurs appliquées par Sigma Paie & RH jusqu'en septembre 2026",
                EstSysteme = true,
                TauxTranche1 = 0.15m,
                TauxTranche2 = 0.30m,
                TauxNuit = 0.50m,
                TauxFerieJour = 0.50m,
                TauxFerieNuit = 1.00m,
                SeuilTranche1 = 8m,
                DureeHebdoLegale = 40m
            };
        }

        /// <summary>
        /// Barème en cinq tranches, celui des états de salaire de rotation (15/35/50/60/120),
        /// applicable à partir de la paie d'octobre 2026.
        /// </summary>
        public static BaremeHeuresSup CinqTranches2026()
        {
            return new BaremeHeuresSup
            {
                DateEffet = new DateTime(2026, 10, 1),
                Libelle = "Heures supplémentaires — cinq tranches",
                Reference = "15 % (41e-48e h), 35 % (au-delà), 50 % nuit, 60 % dimanche/férié jour, 120 % dimanche/férié nuit",
                EstSysteme = true,
                TauxTranche1 = 0.15m,
                TauxTranche2 = 0.35m,
                TauxNuit = 0.50m,
                TauxFerieJour = 0.60m,
                TauxFerieNuit = 1.20m,
                SeuilTranche1 = 8m,
                DureeHebdoLegale = 40m
            };
        }

        /// <summary>Barème le plus récent dont la date d'effet précède ou égale la date donnée.</summary>
        public static BaremeHeuresSup Applicable(IEnumerable<BaremeHeuresSup> baremes, DateTime date)
        {
            BaremeHeuresSup retenu = null;
            if (baremes != null)
            {
                foreach (var b in baremes)
                {
                    if (b == null || b.DateEffet.Date > date.Date) continue;
                    if (retenu == null || b.DateEffet > retenu.DateEffet) retenu = b;
                }
            }
            return retenu ?? Historique();
        }

        // ── Cohérence ────────────────────────────────────────────────────────

        public List<string> Anomalies()
        {
            var anomalies = new List<string>();

            Verifier(anomalies, TauxTranche1, "tranche 1");
            Verifier(anomalies, TauxTranche2, "tranche 2");
            Verifier(anomalies, TauxNuit, "nuit");
            Verifier(anomalies, TauxFerieJour, "dimanche/férié de jour");
            Verifier(anomalies, TauxFerieNuit, "dimanche/férié de nuit");

            if (SeuilTranche1 < 0m || SeuilTranche1 > 40m)
                anomalies.Add("Le nombre d'heures de la tranche 1 doit être compris entre 0 et 40.");

            if (DureeHebdoLegale <= 0m || DureeHebdoLegale > 60m)
                anomalies.Add("La durée légale hebdomadaire doit être comprise entre 1 et 60 heures.");

            if (DateEffet.Year < 1990)
                anomalies.Add("La date d'effet n'est pas renseignée.");

            return anomalies;
        }

        private static void Verifier(List<string> anomalies, decimal taux, string nom)
        {
            if (taux < 0m || taux > 3m)
            {
                anomalies.Add(string.Format(
                    "Majoration {0} : {1} est hors de l'intervalle 0 – 300 % " +
                    "(saisir la proportion, 0,35 pour 35 %).", nom, Pourcent(taux)));
            }
        }

        public BaremeHeuresSup Clone()
        {
            return new BaremeHeuresSup
            {
                Id = Id,
                DateEffet = DateEffet,
                Libelle = Libelle,
                Reference = Reference,
                EstSysteme = EstSysteme,
                TauxTranche1 = TauxTranche1,
                TauxTranche2 = TauxTranche2,
                TauxNuit = TauxNuit,
                TauxFerieJour = TauxFerieJour,
                TauxFerieNuit = TauxFerieNuit,
                SeuilTranche1 = SeuilTranche1,
                DureeHebdoLegale = DureeHebdoLegale
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

        protected virtual void OnPropertyChanged([CallerMemberName] string nom = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
        }
    }
}

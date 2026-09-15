using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Une tranche du barème progressif de l'IUTS (art. 112 du CGI).
    ///
    /// Les bornes sont CONTIGÜES : la borne inférieure d'une tranche est exactement la
    /// borne supérieure de la précédente. Le texte publie « 30.100 à 50.000 » mais ce
    /// 30.100 n'est qu'un repère informel — l'utiliser comme borne de calcul ferait
    /// perdre 100 FCFA de largeur à chaque changement de tranche.
    /// </summary>
    public class TrancheBareme : INotifyPropertyChanged
    {
        private int _ordre;
        private decimal _borneInferieure;
        private decimal _borneSuperieure;
        private decimal _taux;

        /// <summary>Rang de la tranche, de la plus basse à la plus haute.</summary>
        public int Ordre
        {
            get { return _ordre; }
            set { if (_ordre != value) { _ordre = value; OnPropertyChanged(); } }
        }

        /// <summary>Borne inférieure incluse, en FCFA.</summary>
        public decimal BorneInferieure
        {
            get { return _borneInferieure; }
            set
            {
                if (_borneInferieure != value)
                {
                    _borneInferieure = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Designation));
                }
            }
        }

        /// <summary>
        /// Borne supérieure, en FCFA. <see cref="decimal.MaxValue"/> pour la dernière
        /// tranche, qui n'est pas plafonnée.
        /// </summary>
        public decimal BorneSuperieure
        {
            get { return _borneSuperieure; }
            set
            {
                if (_borneSuperieure != value)
                {
                    _borneSuperieure = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Designation));
                    OnPropertyChanged(nameof(EstDerniereTranche));
                    OnPropertyChanged(nameof(BorneSuperieureSaisie));
                }
            }
        }

        /// <summary>
        /// Borne supérieure telle qu'elle se saisit dans la grille. Une case laissée vide
        /// signifie « et au-delà » : c'est la seule façon lisible d'exprimer une tranche
        /// ouverte, puisque personne ne va taper à la main la borne technique qui la
        /// représente en base.
        /// </summary>
        public string BorneSuperieureSaisie
        {
            get
            {
                return EstDerniereTranche
                    ? string.Empty
                    : BorneSuperieure.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    BorneSuperieure = decimal.MaxValue;
                    return;
                }

                decimal saisie;
                string nettoye = value.Replace(" ", "").Replace(" ", "");

                if (decimal.TryParse(nettoye, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.CurrentCulture, out saisie) ||
                    decimal.TryParse(nettoye, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out saisie))
                {
                    BorneSuperieure = saisie;
                }

                // Une saisie illisible laisse la valeur précédente en place : mieux vaut
                // ne rien changer que d'écraser une borne par zéro.
                OnPropertyChanged();
            }
        }

        /// <summary>Taux en POURCENTAGE (12,10 et non 0,121).</summary>
        public decimal Taux
        {
            get { return _taux; }
            set
            {
                if (_taux != value)
                {
                    _taux = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Designation));
                }
            }
        }

        public bool EstDerniereTranche
        {
            get { return BorneSuperieure >= decimal.MaxValue / 2m; }
        }

        /// <summary>Libellé de la tranche tel qu'il se lit dans le texte.</summary>
        public string Designation
        {
            get
            {
                string haut = EstDerniereTranche
                    ? "et au-delà"
                    : string.Format("à {0:N0}", BorneSuperieure);
                return string.Format("{0:N0} {1} — {2:N2} %", BorneInferieure, haut, Taux);
            }
        }

        public TrancheBareme Clone()
        {
            return new TrancheBareme
            {
                Ordre = Ordre,
                BorneInferieure = BorneInferieure,
                BorneSuperieure = BorneSuperieure,
                Taux = Taux
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Réduction d'impôt pour charges de famille (art. 113 du CGI), exprimée en
    /// proportion de l'IUTS brut.
    /// </summary>
    public class ReductionChargeFamille : INotifyPropertyChanged
    {
        private int _nombreCharges;
        private decimal _taux;

        /// <summary>Nombre de personnes à charge, de 0 à 7.</summary>
        public int NombreCharges
        {
            get { return _nombreCharges; }
            set
            {
                if (_nombreCharges != value)
                {
                    _nombreCharges = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Designation));
                }
            }
        }

        /// <summary>Taux en PROPORTION (0,08 pour 8 %).</summary>
        public decimal Taux
        {
            get { return _taux; }
            set
            {
                if (_taux != value)
                {
                    _taux = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Designation));
                }
            }
        }

        public string Designation
        {
            get
            {
                string charges = NombreCharges == 0
                    ? "Aucune charge"
                    : (NombreCharges >= 7
                        ? "7 charges et plus"
                        : string.Format("{0} charge{1}", NombreCharges, NombreCharges > 1 ? "s" : ""));
                return string.Format("{0} — {1:P0}", charges, Taux);
            }
        }

        public ReductionChargeFamille Clone()
        {
            return new ReductionChargeFamille { NombreCharges = NombreCharges, Taux = Taux };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Un jeu complet de paramètres légaux, valable à partir d'une DATE D'EFFET
    /// (module SOC-02 de la feuille de route).
    ///
    /// Jusqu'ici les taux et plafonds étaient soit écrits en dur dans le moteur de calcul
    /// (CNSS 5,5 % / 16 %, plafond 800 000), soit lus dans des fichiers JSON sans
    /// dimension temporelle. Conséquence : le jour où un taux change, recalculer ou
    /// réimprimer un bulletin d'un mois antérieur donne un résultat FAUX, puisqu'on lui
    /// applique le taux d'aujourd'hui. C'est exactement ce que cette table empêche.
    ///
    /// Un barème n'est jamais modifié quand la loi change : on en DUPLIQUE un nouveau
    /// avec une nouvelle date d'effet. Le barème applicable à une période est celui dont
    /// la date d'effet est la plus récente parmi celles antérieures ou égales à la
    /// période.
    ///
    /// Note d'architecture : ce module constitue le référentiel daté. Le branchement du
    /// moteur de calcul sur ce référentiel est une étape distincte et volontairement
    /// séparée, pour que le moteur IUTS audité ne soit pas modifié en même temps que le
    /// stockage de ses paramètres.
    /// </summary>
    public class BaremeLegal : INotifyPropertyChanged
    {
        private int _id;
        private DateTime _dateEffet;
        private string _libelle;
        private string _reference;
        private string _commentaire;
        private bool _estSysteme;

        private decimal _tauxCnssSalarie;
        private decimal _tauxCnssPrestationsFamiliales;
        private decimal _tauxCnssRisquesProfessionnels;
        private decimal _tauxCnssPensionsPatronal;
        private decimal _plafondCnssMensuel;

        private decimal _tauxTaxePatronaleApprentissage;

        private decimal _tauxMaxIndemniteLogement;
        private decimal _plafondIndemniteLogement;
        private decimal _tauxMaxIndemniteTransport;
        private decimal _plafondIndemniteTransport;
        private decimal _tauxMaxIndemniteFonction;
        private decimal _plafondIndemniteFonction;

        private decimal _tauxMaxCotisationRetraiteDeductible;
        private decimal _abattementCategoriesSuperieures;
        private decimal _abattementAutresCategories;

        // ── Identité ─────────────────────────────────────────────────────────

        /// <summary>Identifiant technique. 0 tant que le barème n'a pas été enregistré.</summary>
        public int Id
        {
            get { return _id; }
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        /// <summary>Premier jour à partir duquel ce jeu de paramètres s'applique.</summary>
        public DateTime DateEffet
        {
            get { return _dateEffet; }
            set
            {
                if (_dateEffet != value)
                {
                    _dateEffet = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Designation));
                    OnPropertyChanged(nameof(DateEffetLibelle));
                }
            }
        }

        public string Libelle
        {
            get { return _libelle; }
            set
            {
                if (_libelle != value)
                {
                    _libelle = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Designation));
                }
            }
        }

        /// <summary>Texte légal d'origine : loi de finances, arrêté, décret.</summary>
        public string Reference
        {
            get { return _reference; }
            set { if (_reference != value) { _reference = value; OnPropertyChanged(); } }
        }

        public string Commentaire
        {
            get { return _commentaire; }
            set { if (_commentaire != value) { _commentaire = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Barème livré avec l'application. Il reste modifiable — un texte peut avoir été
        /// mal transcrit — mais l'écran le signale pour éviter une correction à l'aveugle.
        /// </summary>
        public bool EstSysteme
        {
            get { return _estSysteme; }
            set { if (_estSysteme != value) { _estSysteme = value; OnPropertyChanged(); } }
        }

        // ── Cotisations sociales (CNSS) ──────────────────────────────────────

        /// <summary>Part salariale, en proportion (0,055 pour 5,5 %).</summary>
        public decimal TauxCnssSalarie
        {
            get { return _tauxCnssSalarie; }
            set
            {
                if (_tauxCnssSalarie != value)
                {
                    _tauxCnssSalarie = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TauxCnssEmployeurTotal));
                    OnPropertyChanged(nameof(TauxCnssGlobal));
                }
            }
        }

        /// <summary>Part patronale — prestations familiales (0,06).</summary>
        public decimal TauxCnssPrestationsFamiliales
        {
            get { return _tauxCnssPrestationsFamiliales; }
            set
            {
                if (_tauxCnssPrestationsFamiliales != value)
                {
                    _tauxCnssPrestationsFamiliales = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TauxCnssEmployeurTotal));
                    OnPropertyChanged(nameof(TauxCnssGlobal));
                }
            }
        }

        /// <summary>Part patronale — risques professionnels (0,015).</summary>
        public decimal TauxCnssRisquesProfessionnels
        {
            get { return _tauxCnssRisquesProfessionnels; }
            set
            {
                if (_tauxCnssRisquesProfessionnels != value)
                {
                    _tauxCnssRisquesProfessionnels = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TauxCnssEmployeurTotal));
                    OnPropertyChanged(nameof(TauxCnssGlobal));
                }
            }
        }

        /// <summary>Part patronale — pensions (0,085).</summary>
        public decimal TauxCnssPensionsPatronal
        {
            get { return _tauxCnssPensionsPatronal; }
            set
            {
                if (_tauxCnssPensionsPatronal != value)
                {
                    _tauxCnssPensionsPatronal = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TauxCnssEmployeurTotal));
                    OnPropertyChanged(nameof(TauxCnssGlobal));
                }
            }
        }

        /// <summary>Plafond mensuel de l'assiette CNSS, en FCFA.</summary>
        public decimal PlafondCnssMensuel
        {
            get { return _plafondCnssMensuel; }
            set { if (_plafondCnssMensuel != value) { _plafondCnssMensuel = value; OnPropertyChanged(); } }
        }

        /// <summary>Somme des trois branches patronales.</summary>
        public decimal TauxCnssEmployeurTotal
        {
            get
            {
                return TauxCnssPrestationsFamiliales
                     + TauxCnssRisquesProfessionnels
                     + TauxCnssPensionsPatronal;
            }
        }

        /// <summary>Coût social total du salaire plafonné, parts salariale et patronale réunies.</summary>
        public decimal TauxCnssGlobal
        {
            get { return TauxCnssSalarie + TauxCnssEmployeurTotal; }
        }

        // ── Taxe patronale d'apprentissage ───────────────────────────────────

        /// <summary>TPA, en proportion de la masse salariale.</summary>
        public decimal TauxTaxePatronaleApprentissage
        {
            get { return _tauxTaxePatronaleApprentissage; }
            set { if (_tauxTaxePatronaleApprentissage != value) { _tauxTaxePatronaleApprentissage = value; OnPropertyChanged(); } }
        }

        // ── Exonérations d'indemnités (art. 106 du CGI) ──────────────────────

        /// <summary>Part exonérée de l'indemnité de logement, en proportion du brut.</summary>
        public decimal TauxMaxIndemniteLogement
        {
            get { return _tauxMaxIndemniteLogement; }
            set { if (_tauxMaxIndemniteLogement != value) { _tauxMaxIndemniteLogement = value; OnPropertyChanged(); } }
        }

        /// <summary>Plafond en valeur de l'exonération de logement, en FCFA.</summary>
        public decimal PlafondIndemniteLogement
        {
            get { return _plafondIndemniteLogement; }
            set { if (_plafondIndemniteLogement != value) { _plafondIndemniteLogement = value; OnPropertyChanged(); } }
        }

        public decimal TauxMaxIndemniteTransport
        {
            get { return _tauxMaxIndemniteTransport; }
            set { if (_tauxMaxIndemniteTransport != value) { _tauxMaxIndemniteTransport = value; OnPropertyChanged(); } }
        }

        public decimal PlafondIndemniteTransport
        {
            get { return _plafondIndemniteTransport; }
            set { if (_plafondIndemniteTransport != value) { _plafondIndemniteTransport = value; OnPropertyChanged(); } }
        }

        public decimal TauxMaxIndemniteFonction
        {
            get { return _tauxMaxIndemniteFonction; }
            set { if (_tauxMaxIndemniteFonction != value) { _tauxMaxIndemniteFonction = value; OnPropertyChanged(); } }
        }

        public decimal PlafondIndemniteFonction
        {
            get { return _plafondIndemniteFonction; }
            set { if (_plafondIndemniteFonction != value) { _plafondIndemniteFonction = value; OnPropertyChanged(); } }
        }

        // ── Déductions sur la base imposable ─────────────────────────────────

        /// <summary>Part déductible d'une cotisation de retraite complémentaire.</summary>
        public decimal TauxMaxCotisationRetraiteDeductible
        {
            get { return _tauxMaxCotisationRetraiteDeductible; }
            set { if (_tauxMaxCotisationRetraiteDeductible != value) { _tauxMaxCotisationRetraiteDeductible = value; OnPropertyChanged(); } }
        }

        /// <summary>Abattement professionnel des catégories supérieures (0,20).</summary>
        public decimal AbattementCategoriesSuperieures
        {
            get { return _abattementCategoriesSuperieures; }
            set { if (_abattementCategoriesSuperieures != value) { _abattementCategoriesSuperieures = value; OnPropertyChanged(); } }
        }

        /// <summary>Abattement professionnel des autres catégories (0,25).</summary>
        public decimal AbattementAutresCategories
        {
            get { return _abattementAutresCategories; }
            set { if (_abattementAutresCategories != value) { _abattementAutresCategories = value; OnPropertyChanged(); } }
        }

        // ── Collections ──────────────────────────────────────────────────────

        /// <summary>Tranches de l'IUTS, de la plus basse à la plus haute.</summary>
        public ObservableCollection<TrancheBareme> Tranches { get; private set; }

        /// <summary>Réductions pour charges de famille, de 0 à 7 charges.</summary>
        public ObservableCollection<ReductionChargeFamille> ChargesFamille { get; private set; }

        public BaremeLegal()
        {
            Tranches = new ObservableCollection<TrancheBareme>();
            ChargesFamille = new ObservableCollection<ReductionChargeFamille>();
            _dateEffet = new DateTime(DateTime.Today.Year, 1, 1);
        }

        // ── Libellés d'affichage ─────────────────────────────────────────────

        public string DateEffetLibelle
        {
            get { return string.Format("Applicable à partir du {0:dd/MM/yyyy}", DateEffet); }
        }

        public string Designation
        {
            get
            {
                string libelle = string.IsNullOrWhiteSpace(Libelle) ? "Barème" : Libelle;
                return string.Format("{0} — {1:dd/MM/yyyy}", libelle, DateEffet);
            }
        }

        public int NombreTranches
        {
            get { return Tranches == null ? 0 : Tranches.Count; }
        }

        // ── Cohérence ────────────────────────────────────────────────────────

        /// <summary>
        /// Contrôles qu'un barème doit passer avant d'être enregistré. Un barème
        /// incohérent produirait des bulletins faux sans le moindre message d'erreur :
        /// mieux vaut le refuser à la saisie.
        /// </summary>
        public List<string> Anomalies()
        {
            var anomalies = new List<string>();

            if (Tranches == null || Tranches.Count == 0)
            {
                anomalies.Add("Le barème ne comporte aucune tranche.");
                return anomalies;
            }

            var ordonnees = Tranches.OrderBy(t => t.Ordre).ThenBy(t => t.BorneInferieure).ToList();

            if (ordonnees[0].BorneInferieure != 0m)
                anomalies.Add("La première tranche doit commencer à 0.");

            for (int i = 0; i < ordonnees.Count; i++)
            {
                var tranche = ordonnees[i];

                if (tranche.BorneSuperieure <= tranche.BorneInferieure)
                {
                    anomalies.Add(string.Format(
                        "Tranche {0} : la borne supérieure ({1:N0}) n'est pas au-dessus de la borne inférieure ({2:N0}).",
                        i + 1, tranche.BorneSuperieure, tranche.BorneInferieure));
                }

                if (tranche.Taux < 0m || tranche.Taux > 100m)
                {
                    anomalies.Add(string.Format(
                        "Tranche {0} : le taux {1:N2} % est hors de l'intervalle 0 – 100 %.",
                        i + 1, tranche.Taux));
                }

                if (i > 0 && ordonnees[i - 1].BorneSuperieure != tranche.BorneInferieure)
                {
                    anomalies.Add(string.Format(
                        "Tranches {0} et {1} : les bornes ne se touchent pas ({2:N0} puis {3:N0}). " +
                        "Les revenus compris entre les deux ne seraient imposés nulle part.",
                        i, i + 1, ordonnees[i - 1].BorneSuperieure, tranche.BorneInferieure));
                }
            }

            if (!ordonnees[ordonnees.Count - 1].EstDerniereTranche)
            {
                anomalies.Add(
                    "La dernière tranche doit être ouverte : sans elle, les revenus au-delà " +
                    "de la dernière borne ne seraient pas imposés.");
            }

            if (PlafondCnssMensuel <= 0m)
                anomalies.Add("Le plafond CNSS mensuel doit être renseigné.");

            if (TauxCnssSalarie < 0m || TauxCnssSalarie > 1m)
                anomalies.Add("Le taux CNSS salarié doit être une proportion entre 0 et 1.");

            if (TauxCnssEmployeurTotal < 0m || TauxCnssEmployeurTotal > 1m)
                anomalies.Add("Le total des taux CNSS patronaux doit être une proportion entre 0 et 1.");

            return anomalies;
        }

        public BaremeLegal Clone()
        {
            var copie = new BaremeLegal
            {
                Id = Id,
                DateEffet = DateEffet,
                Libelle = Libelle,
                Reference = Reference,
                Commentaire = Commentaire,
                EstSysteme = EstSysteme,
                TauxCnssSalarie = TauxCnssSalarie,
                TauxCnssPrestationsFamiliales = TauxCnssPrestationsFamiliales,
                TauxCnssRisquesProfessionnels = TauxCnssRisquesProfessionnels,
                TauxCnssPensionsPatronal = TauxCnssPensionsPatronal,
                PlafondCnssMensuel = PlafondCnssMensuel,
                TauxTaxePatronaleApprentissage = TauxTaxePatronaleApprentissage,
                TauxMaxIndemniteLogement = TauxMaxIndemniteLogement,
                PlafondIndemniteLogement = PlafondIndemniteLogement,
                TauxMaxIndemniteTransport = TauxMaxIndemniteTransport,
                PlafondIndemniteTransport = PlafondIndemniteTransport,
                TauxMaxIndemniteFonction = TauxMaxIndemniteFonction,
                PlafondIndemniteFonction = PlafondIndemniteFonction,
                TauxMaxCotisationRetraiteDeductible = TauxMaxCotisationRetraiteDeductible,
                AbattementCategoriesSuperieures = AbattementCategoriesSuperieures,
                AbattementAutresCategories = AbattementAutresCategories
            };

            foreach (var tranche in Tranches)
                copie.Tranches.Add(tranche.Clone());

            foreach (var charge in ChargesFamille)
                copie.ChargesFamille.Add(charge.Clone());

            return copie;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

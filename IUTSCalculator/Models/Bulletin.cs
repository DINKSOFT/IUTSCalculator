using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente un bulletin de paie mensuel complet pour un employé, tel que défini
    /// par le modèle de référence (fichier "model_buletin.xlsx", onglet "Option 4").
    /// C'est un instantané (snapshot) : une fois généré, ses valeurs ne changent plus,
    /// même si l'employé ou les paramètres fiscaux sont modifiés par la suite.
    /// </summary>
    public class Bulletin
    {
        #region Entreprise (dossier émetteur du bulletin)

        public string EntrepriseRaisonSociale { get; set; }
        public string EntrepriseAdresse { get; set; }
        public string EntrepriseVille { get; set; }
        public string EntrepriseTelephone { get; set; }
        public string EntrepriseIFU { get; set; }
        public string EntrepriseRCCM { get; set; }
        public string EntrepriseRegimeFiscal { get; set; }

        /// <summary>Chemin complet vers le fichier image du logo de l'entreprise (optionnel)</summary>
        public string EntrepriseLogoPath { get; set; }

        /// <summary>Indique si un logo valide (chemin renseigné et fichier existant) est disponible</summary>
        [JsonIgnore]
        public bool HasLogo => !string.IsNullOrWhiteSpace(EntrepriseLogoPath) && File.Exists(EntrepriseLogoPath);

        /// <summary>Inverse de <see cref="HasLogo"/> (utilisé pour afficher l'emplacement réservé au logo)</summary>
        [JsonIgnore]
        public bool NoLogo => !HasLogo;

        /// <summary>Adresse et ville de l'entreprise sur une seule ligne (ex. "12 Av. Kwamé N'Krumah, Ouagadougou")</summary>
        [JsonIgnore]
        public string EntrepriseAdresseLigne =>
            string.Join(", ", new[] { EntrepriseAdresse, EntrepriseVille }.Where(s => !string.IsNullOrWhiteSpace(s)));

        /// <summary>IFU et RCCM de l'entreprise sur une seule ligne</summary>
        [JsonIgnore]
        public string EntrepriseIdentifiants =>
            string.Join("  —  ", new[]
            {
                string.IsNullOrWhiteSpace(EntrepriseIFU) ? null : $"IFU : {EntrepriseIFU}",
                string.IsNullOrWhiteSpace(EntrepriseRCCM) ? null : $"RCCM : {EntrepriseRCCM}"
            }.Where(s => s != null));

        #endregion

        #region Identité / période

        public string EmployeeId { get; set; }
        public string NomEmploye { get; set; }
        public string PrenomEmploye { get; set; }
        public string Emploi { get; set; }
        public string Categorie { get; set; }

        public DateTime? DateEmbauche { get; set; }

        /// <summary>Premier jour de la période de paie (ex. 1er du mois)</summary>
        public DateTime PeriodeDebut { get; set; }

        /// <summary>Dernier jour de la période de paie (ex. dernier jour du mois)</summary>
        public DateTime PeriodeFin { get; set; }

        /// <summary>Ancienneté en mois complets, calculée à la date de fin de période</summary>
        public int AncienneteMois { get; set; }

        /// <summary>Congés acquis pendant la période (Ancienneté(mois) × 2,5 jours, cf. modèle Option 4)</summary>
        public decimal CongesAcquisJours { get; set; }

        /// <summary>Congés pris pendant la période (saisie manuelle)</summary>
        public decimal CongesPrisJours { get; set; }

        /// <summary>Congés cumulatifs acquis = Acquis − Pris</summary>
        public decimal CongesCumulesJours { get; set; }

        public int ChargesFamiliales { get; set; }
        public bool ConjointTravaille { get; set; }

        #endregion

        #region Gains

        public decimal SalaireDeBase { get; set; }
        public decimal IndemniteLogement { get; set; }
        public decimal IndemniteTransport { get; set; }
        public decimal IndemniteFonction { get; set; }
        public decimal IndemniteSujetion { get; set; }

        /// <summary>Salaire brut = salaire de base + toutes les indemnités + gains variables du mois</summary>
        public decimal SalaireBrut { get; set; }

        #endregion

        #region Variables du mois (PAI-02)

        /// <summary>
        /// Les variables saisies pour ce salarié sur cette période : heures supplémentaires,
        /// primes, acomptes, oppositions… Vide quand rien n'a été saisi, auquel cas le
        /// bulletin est en tout point celui qu'on obtenait avant ce module.
        ///
        /// Ces lignes ne sont pas décoratives : elles expliquent l'écart entre la somme des
        /// rubriques permanentes et le salaire brut. Un bulletin dont le brut ne se
        /// décompose pas n'est pas opposable au salarié.
        /// </summary>
        public List<LigneBulletinVariable> LignesVariables { get; set; }
            = new List<LigneBulletinVariable>();

        /// <summary>Total des gains variables du mois (compris dans <see cref="SalaireBrut"/>).</summary>
        public decimal TotalGainsVariables { get; set; }

        /// <summary>
        /// Retenue pour absence non rémunérée (module PAI-04), déjà déduite du
        /// <see cref="SalaireBrut"/>. Affichée en clair sur le bulletin : un brut
        /// inférieur au salaire contractuel sans ligne pour l'expliquer est la première
        /// cause de contestation d'un bulletin.
        /// </summary>
        public decimal RetenueAbsences { get; set; }

        /// <summary>Libellé de cette retenue (« Absence non rémunérée (3 j) »).</summary>
        public string LibelleRetenueAbsences { get; set; }

        /// <summary>Vrai si une absence non rémunérée pèse sur ce bulletin.</summary>
        [JsonIgnore]
        public bool ADesAbsencesRetenues
        {
            get { return RetenueAbsences > 0m; }
        }

        /// <summary>
        /// Total des retenues diverses du mois, déduites du net après impôt et cotisations
        /// (acompte, prêt, opposition, cotisation syndicale).
        /// </summary>
        public decimal TotalRetenuesDiverses { get; set; }

        /// <summary>Vrai si des variables ont été saisies : pilote l'affichage des sections dédiées.</summary>
        [JsonIgnore]
        public bool ADesVariables
        {
            get { return LignesVariables != null && LignesVariables.Count > 0; }
        }

        /// <summary>Gains variables seuls, dans l'ordre de saisie.</summary>
        [JsonIgnore]
        public List<LigneBulletinVariable> GainsVariables
        {
            get
            {
                return (LignesVariables ?? new List<LigneBulletinVariable>())
                    .Where(l => !l.EstRetenue).ToList();
            }
        }

        /// <summary>Retenues diverses seules, dans l'ordre de saisie.</summary>
        [JsonIgnore]
        public List<LigneBulletinVariable> RetenuesVariables
        {
            get
            {
                return (LignesVariables ?? new List<LigneBulletinVariable>())
                    .Where(l => l.EstRetenue).ToList();
            }
        }

        /// <summary>Vrai si au moins un gain variable figure sur le bulletin.</summary>
        [JsonIgnore]
        public bool ADesGainsVariables
        {
            get { return GainsVariables.Count > 0; }
        }

        /// <summary>Vrai si au moins une retenue diverse figure sur le bulletin.</summary>
        [JsonIgnore]
        public bool ADesRetenuesVariables
        {
            get { return RetenuesVariables.Count > 0; }
        }

        /// <summary>
        /// Rémunération permanente : salaire de base et indemnités contractuelles, hors
        /// variables du mois. C'est le sous-total qui rend le brut vérifiable.
        /// </summary>
        [JsonIgnore]
        public decimal TotalRemunerationPermanente
        {
            get
            {
                return SalaireDeBase + IndemniteLogement + IndemniteTransport
                     + IndemniteFonction + IndemniteSujetion;
            }
        }

        /// <summary>
        /// Congés acquis restants à la fin de la période, tels que le compteur PAI-04 les
        /// arrête. Zéro tant qu'aucune absence n'est enregistrée, auquel cas le bulletin
        /// garde le décompte estimé d'après l'ancienneté.
        /// </summary>
        public decimal SoldeCongesJours { get; set; }

        #endregion

        #region Cotisations et impôt (Art. 106/109/111/112/113 CGI)

        public decimal CnssEmploye { get; set; }
        public decimal CnssEmployeur { get; set; }

        /// <summary>Total des franchises/exonérations sur indemnités logement/fonction/transport (Art.106)</summary>
        public decimal TotalExonerations { get; set; }

        /// <summary>Abattement forfaitaire pour frais professionnels (20% ou 25% du salaire de base, Art.111)</summary>
        public decimal AbattementForfaitaire { get; set; }

        /// <summary>Base IUTS (base imposable arrondie à la centaine de FCFA inférieure, Art.112)</summary>
        public decimal BaseIUTS { get; set; }

        /// <summary>IUTS brut, avant réduction pour charges familiales</summary>
        public decimal IUTSBrut { get; set; }

        /// <summary>Réduction d'impôt pour charges familiales (Art.113)</summary>
        public decimal AbattementChargesFamiliales { get; set; }

        /// <summary>Net IUTS = IUTS brut − réduction pour charges familiales</summary>
        public decimal NetIUTS { get; set; }

        /// <summary>Retenue obligatoire sur salaire (1% du résidu après IUTS et CNSS)</summary>
        public decimal RetenueObligatoire { get; set; }

        #endregion

        #region Résultat

        public decimal SalaireNetAPayer { get; set; }

        #endregion

        #region Règlement / signatures

        public string ModeReglement { get; set; }
        public string NumeroCompte { get; set; }

        #endregion

        #region Cumuls annuels (depuis le 1er janvier de la période)

        /// <summary>Cumul du salaire brut depuis janvier jusqu'à la fin de la période (inclus)</summary>
        public decimal CumulBrutAnnuel { get; set; }

        /// <summary>Cumul du Net IUTS depuis janvier jusqu'à la fin de la période (inclus)</summary>
        public decimal CumulIUTSAnnuel { get; set; }

        /// <summary>Cumul de la CNSS part employé depuis janvier jusqu'à la fin de la période (inclus)</summary>
        public decimal CumulCnssEmployeAnnuel { get; set; }

        /// <summary>Cumul du salaire net à payer depuis janvier jusqu'à la fin de la période (inclus)</summary>
        public decimal CumulNetAnnuel { get; set; }

        /// <summary>
        /// Indique que les cumuls annuels ci-dessus sont estimés (rémunération du mois en cours
        /// répétée sur les mois précédents de l'année), faute d'historique de paie mensuel
        /// détaillé disponible pour cet employé — plutôt que recalculés à partir de bulletins
        /// réellement générés chaque mois.
        /// </summary>
        public bool CumulsEstimes { get; set; }

        #endregion

        #region Contrôle / authenticité

        /// <summary>
        /// Numéro de contrôle unique de ce bulletin, calculé à partir de ses données
        /// (employé, période, montants, date de génération). Imprimé (en texte, et sous
        /// forme de QR code sur le PDF) pour permettre à l'employé ou à un tiers de
        /// vérifier l'authenticité du document.
        /// </summary>
        public string NumeroControle { get; set; }

        #endregion

        /// <summary>Date/heure à laquelle ce bulletin a été généré</summary>
        public DateTime DateGeneration { get; set; } = DateTime.Now;

        [JsonIgnore]
        public string NomComplet => $"{NomEmploye} {PrenomEmploye}".Trim();

        /// <summary>Libellé de période au format "01 au 28 Février 2024", comme sur le modèle de référence</summary>
        [JsonIgnore]
        public string PeriodeLibelle
        {
            get
            {
                var culture = new CultureInfo("fr-FR");
                string moisAnnee = PeriodeFin.ToString("MMMM yyyy", culture);
                moisAnnee = culture.TextInfo.ToTitleCase(moisAnnee);
                return $"{PeriodeDebut:dd} au {PeriodeFin:dd} {moisAnnee}";
            }
        }
    }

    /// <summary>
    /// Une variable du mois telle qu'elle figure sur le bulletin (PAI-02) : un instantané
    /// de la saisie, figé au moment de la génération comme le reste du bulletin.
    /// </summary>
    public class LigneBulletinVariable
    {
        /// <summary>Code de la rubrique (HSUP, PRIME, ACOMPTE…).</summary>
        public string Code { get; set; }

        /// <summary>Libellé affiché, quantité comprise le cas échéant : « Heures supplémentaires (12) ».</summary>
        public string Libelle { get; set; }

        /// <summary>Montant en valeur absolue ; le sens est porté par <see cref="EstRetenue"/>.</summary>
        public decimal Montant { get; set; }

        /// <summary>Vrai pour une retenue sur net, faux pour un gain.</summary>
        public bool EstRetenue { get; set; }

        /// <summary>Traitement fiscal et social, en clair : « IUTS + CNSS », « Non soumis »…</summary>
        public string Traitement { get; set; }

        /// <summary>Montant signé, pour les affichages qui mélangent gains et retenues.</summary>
        [JsonIgnore]
        public decimal MontantSigne
        {
            get { return EstRetenue ? -Montant : Montant; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IUTSCalculator.Models;

namespace IUTSCalculator.Services
{
    /// <summary>
    /// Une ligne du fichier CSV une fois interprétée : l'employé qu'elle décrit (si la
    /// ligne est valide), et les anomalies rencontrées.
    ///
    /// Une ERREUR bloque la ligne (elle n'est pas importée) — réservée aux cas où la
    /// donnée serait fausse plutôt qu'absente (Nom/Prénom manquants, montant illisible).
    /// Un AVERTISSEMENT n'empêche pas l'import : le champ concerné est simplement
    /// ignoré ou ramené à sa valeur par défaut.
    /// </summary>
    public class LigneImportEmploye
    {
        public int NumeroLigne { get; set; }
        public Employee Employe { get; set; }

        /// <summary>Vrai si le Matricule de la ligne correspondait déjà à un employé du dossier.</summary>
        public bool EstMiseAJour { get; set; }

        public List<string> Erreurs { get; } = new List<string>();
        public List<string> Avertissements { get; } = new List<string>();

        public bool EstValide => Erreurs.Count == 0;
    }

    /// <summary>Résultat brut de la lecture d'un fichier CSV, avant écriture en base.</summary>
    public class ResultatLectureCsv
    {
        public List<LigneImportEmploye> Lignes { get; } = new List<LigneImportEmploye>();

        /// <summary>Anomalies qui empêchent toute lecture (fichier vide, illisible, en-têtes obligatoires absentes).</summary>
        public List<string> ErreursGlobales { get; } = new List<string>();

        /// <summary>Anomalies qui n'empêchent pas la lecture (colonnes reconnues mais non utilisées, etc.).</summary>
        public List<string> AvertissementsGlobaux { get; } = new List<string>();
    }

    /// <summary>
    /// Bilan d'un import d'employés (CSV ou JSON), affiché à l'utilisateur par
    /// EmployeeManagementViewModel après l'exécution de DataService.ImportEmployeesFromFileAsync.
    /// </summary>
    public class RapportImportEmployes
    {
        public int Ajoutes { get; set; }
        public int MisAJour { get; set; }
        public int LignesIgnorees { get; set; }
        public List<string> Erreurs { get; } = new List<string>();
        public List<string> Avertissements { get; } = new List<string>();

        public bool AAffecteDesEmployes => Ajoutes > 0 || MisAJour > 0;
    }

    /// <summary>
    /// Lecture (et description) du format CSV d'importation des employés, utilisé par
    /// l'écran « Gestion des employés » → « Importer des employés ».
    ///
    /// Choix de format, pensés pour un CSV rempli dans Excel en français :
    ///   • séparateur point-virgule (un Excel réglé en français utilise déjà la virgule
    ///     comme séparateur décimal dans les montants) ;
    ///   • encodage UTF-8 avec BOM à l'écriture ; à la lecture, UTF-8 ou, à défaut,
    ///     Windows-1252 (l'encodage par défaut d'un « CSV (séparateur point-virgule) »
    ///     enregistré depuis un Excel francophone) ;
    ///   • dates au format JJ/MM/AAAA.
    ///
    /// Les colonnes sont identifiées par leur NOM d'en-tête (insensible à la casse, aux
    /// accents, aux espaces et aux tirets), pas par leur position : l'ordre des colonnes
    /// du modèle peut être changé sans casser l'import, et une colonne absente prend
    /// simplement sa valeur par défaut.
    ///
    /// Une ligne dont le Matricule correspond à un employé déjà présent dans le dossier
    /// REMPLACE intégralement sa fiche (même règle que l'écran salarié : « Modifier »
    /// réécrit tous les champs). Ne pas lister un employé qu'on ne veut pas écraser.
    ///
    /// Une ligne SANS Matricule tente de reconnaître un employé déjà existant par
    /// Nom + Prénom avant de conclure à un nouvel employé — un fichier réimporté
    /// plusieurs fois (le Matricule généré par l'application n'y étant jamais recopié)
    /// met ainsi à jour la même fiche au lieu d'en créer une nouvelle à chaque import.
    /// Si plusieurs employés du dossier partagent exactement ce Nom + Prénom, la
    /// correspondance est jugée trop incertaine : la ligne crée un nouvel employé et un
    /// avertissement le signale, plutôt que de deviner lequel des homonymes mettre à jour.
    /// </summary>
    public static class EmployeeCsvService
    {
        public const char Separateur = ';';

        /// <summary>
        /// En-têtes du modèle, dans l'ordre dans lequel Sigma Paie &amp; RH les propose.
        /// </summary>
        public static readonly string[] EnTetes =
        {
            "Matricule", "Nom", "Prenom", "Sexe", "DateNaissance", "LieuNaissance",
            "Nationalite", "SituationMatrimoniale", "NombreEnfantsACharge", "ConjointTravaille",
            "NumeroCNIB", "DateDelivranceCNIB", "LieuDelivranceCNIB",
            "Adresse", "Telephone", "Email",
            "NumeroCNSS", "NumeroIFU",
            "Categorie", "Service", "Fonction", "DateEntree",
            "SalaireBase", "IndemniteLogement", "IndemniteTransport", "IndemniteFonction", "IndemniteSujetion",
            "Banque", "NumeroCompte", "ModeReglement",
            "ConventionCollective", "Echelon",
            "Statut", "DateSortie", "MotifDepart"
        };

        private static readonly HashSet<string> ClesReconnues =
            new HashSet<string>(EnTetes.Select(CleNormalisee));

        private static readonly string[] FormatsDate =
        {
            "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy"
        };

        /// <summary>
        /// Clé de comparaison insensible à la casse, aux accents, aux espaces et à la
        /// ponctuation courante (« Prénom », « prenom », « Prénom ! » se comparent égaux).
        /// </summary>
        private static string CleNormalisee(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            string formD = s.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsWhiteSpace(c) || c == '_' || c == '-' || c == '\'' || c == '(' || c == ')') continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Lit et interprète un fichier CSV d'employés. Ne touche pas à la base.</summary>
        /// <param name="cheminFichier">Chemin du fichier .csv.</param>
        /// <param name="employesExistants">
        /// Employés déjà présents dans le dossier : sert à distinguer ajout et mise à jour
        /// (par Matricule), et à reconnaître par Nom + Prénom une ligne sans Matricule ;
        /// peut être null.
        /// </param>
        /// <param name="conventionsDisponibles">
        /// Conventions collectives connues du dossier, pour résoudre la colonne
        /// ConventionCollective (par Code ou Libelle) ; peut être null.
        /// </param>
        public static ResultatLectureCsv Lire(
            string cheminFichier,
            IEnumerable<Employee> employesExistants,
            IEnumerable<ConventionCollective> conventionsDisponibles)
        {
            var resultat = new ResultatLectureCsv();

            string[] lignesBrutes;
            try
            {
                lignesBrutes = LireLignesFichier(cheminFichier);
            }
            catch (Exception ex)
            {
                resultat.ErreursGlobales.Add("Fichier illisible : " + ex.Message);
                return resultat;
            }

            int indexEntete = 0;
            while (indexEntete < lignesBrutes.Length && string.IsNullOrWhiteSpace(lignesBrutes[indexEntete]))
                indexEntete++;
            if (indexEntete >= lignesBrutes.Length)
            {
                resultat.ErreursGlobales.Add("Le fichier est vide.");
                return resultat;
            }

            var entetes = DiviserLigne(lignesBrutes[indexEntete]);
            var indexParCle = new Dictionary<string, int>();
            var colonnesIgnorees = new List<string>();
            for (int i = 0; i < entetes.Count; i++)
            {
                string cle = CleNormalisee(entetes[i]);
                if (string.IsNullOrEmpty(cle)) continue;
                if (ClesReconnues.Contains(cle)) indexParCle[cle] = i;
                else colonnesIgnorees.Add(entetes[i]);
            }
            if (!indexParCle.ContainsKey("NOM") || !indexParCle.ContainsKey("PRENOM"))
            {
                resultat.ErreursGlobales.Add(
                    "Colonnes obligatoires introuvables dans l'en-tête : Nom et/ou Prenom.");
                return resultat;
            }
            if (colonnesIgnorees.Count > 0)
            {
                resultat.AvertissementsGlobaux.Add(
                    "Colonnes non reconnues, ignorées : " + string.Join(", ", colonnesIgnorees));
            }

            var conventions = conventionsDisponibles?.ToList() ?? new List<ConventionCollective>();
            var existants = employesExistants?.ToList() ?? new List<Employee>();
            var matriculesExistants = new HashSet<string>(
                existants.Select(e => e.EmployeeId).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            // Reconnaissance par Nom + Prénom d'une ligne sans Matricule (voir doc de la classe).
            var idsParNomPrenom = new Dictionary<string, List<string>>();
            foreach (var e in existants)
            {
                if (string.IsNullOrWhiteSpace(e.EmployeeId)) continue;
                string cle = CleNormalisee(e.LastName) + "|" + CleNormalisee(e.FirstName);
                List<string> liste;
                if (!idsParNomPrenom.TryGetValue(cle, out liste))
                {
                    liste = new List<string>();
                    idsParNomPrenom[cle] = liste;
                }
                liste.Add(e.EmployeeId);
            }

            var matriculesVusDansLeFichier = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int l = indexEntete + 1; l < lignesBrutes.Length; l++)
            {
                string brute = lignesBrutes[l];
                if (string.IsNullOrWhiteSpace(brute)) continue;

                var champs = DiviserLigne(brute);
                int numeroAffiche = l + 1; // 1-based, en-tête inclus — le repère qu'un tableur affiche aussi

                Func<string, string> V = cle =>
                {
                    int idx;
                    if (!indexParCle.TryGetValue(cle, out idx) || idx >= champs.Count) return string.Empty;
                    return champs[idx].Trim();
                };

                var ligne = new LigneImportEmploye { NumeroLigne = numeroAffiche };

                string nom = V("NOM");
                string prenom = V("PRENOM");
                if (string.IsNullOrWhiteSpace(nom) || string.IsNullOrWhiteSpace(prenom))
                {
                    ligne.Erreurs.Add("Nom et Prénom sont obligatoires.");
                    resultat.Lignes.Add(ligne);
                    continue;
                }

                var employe = new Employee { LastName = nom, FirstName = prenom };

                string matricule = V("MATRICULE");
                if (!string.IsNullOrWhiteSpace(matricule))
                {
                    employe.EmployeeId = matricule;
                    ligne.EstMiseAJour = matriculesExistants.Contains(matricule);
                }
                else
                {
                    string cleNomPrenom = CleNormalisee(nom) + "|" + CleNormalisee(prenom);
                    List<string> correspondances;
                    if (idsParNomPrenom.TryGetValue(cleNomPrenom, out correspondances) && correspondances.Count == 1)
                    {
                        employe.EmployeeId = correspondances[0];
                        ligne.EstMiseAJour = true;
                        ligne.Avertissements.Add(
                            "Aucun Matricule fourni : ligne reconnue comme mise à jour de l'employé existant \"" +
                            nom + " " + prenom + "\" par correspondance de Nom + Prénom.");
                    }
                    else if (correspondances != null && correspondances.Count > 1)
                    {
                        ligne.Avertissements.Add(
                            "Aucun Matricule fourni, et " + correspondances.Count + " employés existants portent le nom \"" +
                            nom + " " + prenom + "\" : impossible de déterminer lequel mettre à jour sans ambiguïté. " +
                            "Un nouvel employé a été créé — indiquez le Matricule pour mettre à jour l'un des homonymes.");
                    }
                    // Aucune correspondance : ligne traitée comme un nouvel employé (EmployeeId
                    // reste vide, AddEmployeeAsync lui attribuera un matricule généré).
                }

                if (!string.IsNullOrWhiteSpace(employe.EmployeeId) && !matriculesVusDansLeFichier.Add(employe.EmployeeId))
                {
                    ligne.Avertissements.Add(
                        "Matricule \"" + employe.EmployeeId + "\" déjà utilisé plus haut dans ce fichier : cette ligne écrasera la précédente.");
                }

                employe.Sexe = V("SEXE");
                employe.LieuNaissance = V("LIEUNAISSANCE");
                employe.Nationalite = V("NATIONALITE");
                employe.SituationMatrimoniale = V("SITUATIONMATRIMONIALE");
                employe.NumeroCnib = V("NUMEROCNIB");
                employe.LieuDelivranceCnib = V("LIEUDELIVRANCECNIB");
                employe.Adresse = V("ADRESSE");
                employe.Telephone = V("TELEPHONE");
                employe.Email = V("EMAIL");
                employe.NumeroCnss = V("NUMEROCNSS");
                employe.NumeroIfu = V("NUMEROIFU");
                employe.Category = V("CATEGORIE");
                employe.Service = V("SERVICE");
                employe.Fonction = V("FONCTION");
                employe.Banque = V("BANQUE");
                employe.NumeroCompte = V("NUMEROCOMPTE");
                employe.ModeReglement = V("MODEREGLEMENT");
                employe.Echelon = V("ECHELON");
                employe.MotifDepart = V("MOTIFDEPART");

                DateTime? dateNaissance;
                if (EssayerDate(V("DATENAISSANCE"), out dateNaissance)) employe.DateNaissance = dateNaissance;
                else ligne.Avertissements.Add("DateNaissance illisible, ignorée : \"" + V("DATENAISSANCE") + "\".");

                DateTime? dateDelivrance;
                if (EssayerDate(V("DATEDELIVRANCECNIB"), out dateDelivrance)) employe.DateDelivranceCnib = dateDelivrance;
                else ligne.Avertissements.Add("DateDelivranceCNIB illisible, ignorée : \"" + V("DATEDELIVRANCECNIB") + "\".");

                DateTime? dateEntree;
                if (EssayerDate(V("DATEENTREE"), out dateEntree)) employe.DateEntree = dateEntree ?? default(DateTime);
                else ligne.Avertissements.Add("DateEntree illisible, ignorée : \"" + V("DATEENTREE") + "\".");

                DateTime? dateSortie;
                if (EssayerDate(V("DATESORTIE"), out dateSortie)) employe.DateSortie = dateSortie;
                else ligne.Avertissements.Add("DateSortie illisible, ignorée : \"" + V("DATESORTIE") + "\".");

                int enfants;
                if (EssayerEntier(V("NOMBREENFANTSACHARGE"), out enfants)) employe.FamilyCharges = enfants;
                else ligne.Avertissements.Add("NombreEnfantsACharge illisible, remis à 0 : \"" + V("NOMBREENFANTSACHARGE") + "\".");

                string conjoint = V("CONJOINTTRAVAILLE");
                string conjointCle = CleNormalisee(conjoint);
                if (conjointCle.Length == 0 || conjointCle == "NON" || conjointCle == "FAUX" || conjointCle == "FALSE" || conjointCle == "0")
                {
                    employe.IsSpouseWorking = false;
                }
                else if (conjointCle == "OUI" || conjointCle == "VRAI" || conjointCle == "TRUE" || conjointCle == "1" || conjointCle == "X")
                {
                    employe.IsSpouseWorking = true;
                }
                else
                {
                    employe.IsSpouseWorking = false;
                    ligne.Avertissements.Add("ConjointTravaille non reconnu (\"" + conjoint + "\"), traité comme Non.");
                }

                decimal salaireBase, indLogement, indTransport, indFonction, indSujetion;
                bool montantsValides = true;
                montantsValides &= ExigerDecimal(V("SALAIREBASE"), "SalaireBase", ligne, out salaireBase);
                montantsValides &= ExigerDecimal(V("INDEMNITELOGEMENT"), "IndemniteLogement", ligne, out indLogement);
                montantsValides &= ExigerDecimal(V("INDEMNITETRANSPORT"), "IndemniteTransport", ligne, out indTransport);
                montantsValides &= ExigerDecimal(V("INDEMNITEFONCTION"), "IndemniteFonction", ligne, out indFonction);
                montantsValides &= ExigerDecimal(V("INDEMNITESUJETION"), "IndemniteSujetion", ligne, out indSujetion);
                if (montantsValides)
                {
                    employe.BaseSalary = salaireBase;
                    employe.HousingAllowance = indLogement;
                    employe.TransportAllowance = indTransport;
                    employe.FunctionAllowance = indFonction;
                    employe.SujetionAllowance = indSujetion;
                }

                string statutTexte = V("STATUT");
                string statutCle = CleNormalisee(statutTexte);
                if (statutCle.Length == 0 || statutCle == "ACTIF") employe.StatutSalarie = 0;
                else if (statutCle == "SORTI" || statutCle == "SORTIE" || statutCle == "INACTIF") employe.StatutSalarie = 1;
                else if (statutCle == "SUSPENDU" || statutCle == "SUSPENDUE") employe.StatutSalarie = 2;
                else
                {
                    employe.StatutSalarie = 0;
                    ligne.Avertissements.Add("Statut non reconnu (\"" + statutTexte + "\"), traité comme Actif.");
                }

                string conventionTexte = V("CONVENTIONCOLLECTIVE");
                if (!string.IsNullOrWhiteSpace(conventionTexte))
                {
                    string conventionCle = CleNormalisee(conventionTexte);
                    var trouvee = conventions.FirstOrDefault(c =>
                        CleNormalisee(c.Code) == conventionCle || CleNormalisee(c.Libelle) == conventionCle);
                    if (trouvee != null) employe.ConventionId = trouvee.Id;
                    else ligne.Avertissements.Add("ConventionCollective \"" + conventionTexte + "\" introuvable, non affectée.");
                }

                ligne.Employe = employe;
                resultat.Lignes.Add(ligne);
            }

            return resultat;
        }

        /// <summary>
        /// Analogue à <see cref="EssayerDecimal"/> mais ajoute directement l'erreur à la
        /// ligne (un montant illisible bloque la ligne — on ne devine pas un salaire).
        /// </summary>
        private static bool ExigerDecimal(string texte, string nomChamp, LigneImportEmploye ligne, out decimal resultat)
        {
            if (EssayerDecimal(texte, out resultat)) return true;
            ligne.Erreurs.Add(nomChamp + " illisible : \"" + texte + "\".");
            return false;
        }

        private static bool EssayerDecimal(string valeur, out decimal resultat)
        {
            if (string.IsNullOrWhiteSpace(valeur)) { resultat = 0m; return true; }

            string nettoye = valeur.Replace(" ", "").Replace(" ", "").Replace(",", ".");
            return decimal.TryParse(
                nettoye, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out resultat);
        }

        private static bool EssayerEntier(string valeur, out int resultat)
        {
            resultat = 0;
            if (string.IsNullOrWhiteSpace(valeur)) return true;
            return int.TryParse(valeur.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out resultat);
        }

        private static bool EssayerDate(string valeur, out DateTime? resultat)
        {
            resultat = null;
            if (string.IsNullOrWhiteSpace(valeur)) return true;

            DateTime d;
            if (DateTime.TryParseExact(valeur.Trim(), FormatsDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            {
                resultat = d;
                return true;
            }
            try
            {
                if (DateTime.TryParse(valeur.Trim(), CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out d))
                {
                    resultat = d;
                    return true;
                }
            }
            catch (CultureNotFoundException)
            {
                // Environnement sans la culture fr-FR installée : on s'en tient aux formats explicites ci-dessus.
            }
            return false;
        }

        /// <summary>
        /// Découpe une ligne CSV en champs, en respectant les guillemets (un champ
        /// peut contenir le séparateur ou un guillemet doublé « "" »). Ne gère pas un
        /// saut de ligne à l'intérieur d'un champ entre guillemets.
        /// </summary>
        private static List<string> DiviserLigne(string ligne)
        {
            var champs = new List<string>();
            var courant = new StringBuilder();
            bool dansGuillemets = false;

            for (int i = 0; i < ligne.Length; i++)
            {
                char c = ligne[i];
                if (dansGuillemets)
                {
                    if (c == '"')
                    {
                        if (i + 1 < ligne.Length && ligne[i + 1] == '"') { courant.Append('"'); i++; }
                        else dansGuillemets = false;
                    }
                    else
                    {
                        courant.Append(c);
                    }
                }
                else
                {
                    if (c == '"') dansGuillemets = true;
                    else if (c == Separateur) { champs.Add(courant.ToString()); courant.Clear(); }
                    else courant.Append(c);
                }
            }
            champs.Add(courant.ToString());
            return champs;
        }

        /// <summary>
        /// Lit le fichier en essayant l'UTF-8 (avec ou sans BOM), puis en repli le
        /// Windows-1252 — l'encodage par défaut d'un CSV point-virgule enregistré
        /// depuis un Excel francophone, faute de quoi les accents seraient corrompus.
        /// </summary>
        private static string[] LireLignesFichier(string cheminFichier)
        {
            byte[] octets = File.ReadAllBytes(cheminFichier);

            string texte;
            if (octets.Length >= 3 && octets[0] == 0xEF && octets[1] == 0xBB && octets[2] == 0xBF)
            {
                texte = Encoding.UTF8.GetString(octets, 3, octets.Length - 3);
            }
            else
            {
                try
                {
                    var utf8Strict = new UTF8Encoding(false, true);
                    texte = utf8Strict.GetString(octets);
                }
                catch (DecoderFallbackException)
                {
                    texte = Encoding.GetEncoding(1252).GetString(octets);
                }
            }

            return texte.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }
    }
}

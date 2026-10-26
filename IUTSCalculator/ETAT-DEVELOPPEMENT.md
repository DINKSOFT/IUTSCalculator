# Sigma Paie & RH — état du développement

Ce fichier sert à **reprendre le travail dans une nouvelle conversation** sans repartir de
zéro. Il dit où en est le projet, quelles décisions ont été prises et pourquoi, et ce qui
reste en dette. Il se met à jour à chaque module livré.

Dernière mise à jour : 26 octobre 2026 (version **1.4.38.2**) — apurement des défauts métier restants, ergonomie et
sécurité : annulation de la retenue d'une mise à pied après recours favorable, prorata du 13ᵉ mois au solde de tout compte,
suppression d'un solde qui rétablit le salarié, anti-doublon des rappels, notifications non bloquantes (détail à la fin du §7).
Précédemment, 19 octobre 2026 — livraison du module **RH-07** (« Santé & sécurité au travail »), qui **achève la
Phase 4**. Trois registres : visites médicales (embauche, périodiques, reprise, à la demande) et leurs échéances, avec une
périodicité de 12 mois ajustable par poste (RH-02) ; équipements de protection individuelle remis à chaque salarié (catalogue
avec durée de vie, renouvellements, fiche de dotation à signer) ; accidents du travail, de trajet et maladies
professionnelles (délai de déclaration de **48 heures ouvrables** à la CNSS et à l'inspection du travail, fiche PDF préparant
l'imprimé de la CNSS, arrêt reporté dans les absences, **indemnités journalières des 2/3 à réclamer à la CNSS** — choix du
client : salaire maintenu en entier). Échéancier, registre Excel 5 feuilles. `SchemaCible = 37`.
Plus tôt le même jour : RH-08 (par anticipation), RH-06 « Discipline & procédures » (`SchemaCible = 35`), correction de la
dette signalée (`SchemaCible = 36`), équivalence **8 heures = 1 jour** pour les absences, barre de défilement du planning RH-08.
**Les Phases 1 à 4 sont livrées** ; suite proposée au client (§2, « Prochain »).
En fin de journée, **protection des données lors des mises à jour** (version **1.4.37.1**) : mise à niveau des bases tout ou
rien (copie de travail contrôlée puis substituée), refus d'une base plus récente que le programme, numéro de version
affiché, sauvegarde / restauration de toutes les données du poste (§3, §4, §7 et « Mettre à jour un client » au §8).

---

## 1. Le projet

Application WPF / .NET Framework 4.8, MVVM, SQLite (`System.Data.SQLite`, ADO.NET brut).
Une base par **dossier** (entreprise), sous `%AppData%\...\Dossiers\dossier_<id>.db`.

L'objectif est de passer d'une calculatrice d'IUTS à un vrai logiciel de paie burkinabè,
en suivant une **feuille de route de 53 modules** répartis en 4 phases. Les modules sont
implémentés **dans l'ordre prévu** — c'est une consigne du client, et l'ordre a un sens :
chaque phase ouvre les suivantes.

---

## 2. Où en est le développement

### Livré (Phases 1, 2, 3 et 4 intégralement achevées)

| Module | Objet |
|---|---|
| SOC-01 | Plan de paie (rubriques) |
| SOC-02 | Barèmes légaux datés |
| PAI-01 | Périodes de paie et clôture |
| DEC-01 | Cotisations CNSS ventilées par branche |
| DEC-04 | Déclaration CNSS (bordereau + état nominatif) |
| SAL-01 | Fiche salarié complète, personnes à charge |
| PAI-02 | Variables du mois (saisie + branchement sur le bulletin) |
| PAI-04 | Congés et absences (compteur + retenue sur brut) |
| PAI-05 | Prêts, avances et acomptes (échéancier + retenue sur net) |
| SOC-03 | Profils de paie (rubriques par population + affectation datée) |
| DEC-02 | Taxe patronale et d'apprentissage (assiette exonérable + barème daté) |
| DEC-03 | Déclaration IUTS/TPA (bordereau + état nominatif) |
| ETA-01 | Livre de paie mensuel (tableau croisé salariés × rubriques, export Excel) |
| CPT-01 | Écritures de paie SYSCOHADA (pièce d'OD mensuelle équilibrée, exports Excel et texte) |
| CPT-02 | Ordres de virement bancaire et récapitulatif des règlements (filtrage banque/mode, détection anomalies RIB, exports Excel et CSV) |
| ADM-02 | Journal d'audit et traçabilité des opérations (suivi fin des salaires et primes, traçabilité des actions sensibles, exports ClosedXML et CSV) |
| PAI-03 | Heures supplémentaires et majorations légales (saisie mensuelle, valorisation automatique, intégration bulletin/masse, export Excel) — cinq tranches sur barème daté depuis RH-08 (15/35/50/60/120 dès octobre 2026, 15/30/50/100 avant) |
| SOC-05 | Grilles de salaires et conventions collectives (classification catégorielle, salaires minima garantis, prime d'ancienneté légale CCI-BF, alertes conformité, intégration bulletin) |
| SAL-05 | Solde de tout compte et gestion des départs (décompte de rupture légal burkinabè, indemnités préavis et licenciement, apurement prêts, reçus PDF/Excel, certificat Art. 67, attestation de service) |
| PAI-06 | Saisies-arrêts, cessions et oppositions (calcul par tranches de la quotité saisissable, super-privilège alimentaire, répartition au marc le franc, arrêt auto, exports Excel) |
| PAI-07 | Rappels & régularisations (salaires rétroactifs, primes, trop-perçus, assistant de calcul différentiel, régularisation annuelle d'IUTS de fin d'exercice, inviolabilité des périodes closes, double export ClosedXML) |
| PAI-08 | Avantages en nature (évaluation forfaitaire logement/véhicule/domesticité/eau/électricité/nourriture, réintégration dans la base IUTS/CNSS/TPA, déduction en bas de bulletin, intégration bulletin/masse/Calcul d'IUTS/Calcul en lot) |
| DEC-05 | État annuel des salaires — DADS (cumuls annuels par salarié et par rubrique : brut, base imposable, IUTS retenu, cotisations CNSS/TPA, alimentés par les mois clôturés) |
| PIL-01 | Tableau de bord masse salariale (évolution mensuelle du brut, du net et du coût employeur ; ventilation par catégorie et par service ; alimenté par les mois clôturés ; export Excel 3 feuilles) |
| RH-01 | Organigramme d'entreprise (directions, services, postes et rattachements hiérarchiques N+1, détection de cycles, visualisation arborescente WPF, affectations, double export Excel 3 feuilles et PDF officiel) |
| RH-02 | Postes & fiches de poste (missions, activités, profil, compétences 1-4, classification conventionnelle, effectif budgété vs pourvu, 4 statuts GPEC, exports ClosedXML 3 feuilles et fiche PDF officielle A4) |
| RH-03 | Entretiens & évaluations (campagnes annuelles, grilles d'évaluation multicritères, objectifs N-1/N+1, notation pondérée sur 5, verrouillage d'immutabilité dès validation, recensement besoins de formation RH-04, double export PDF tripartite et Excel 4 feuilles) |
| RH-04 | Formation (plans annuels de formation, catalogue avec compétences RH-02, organismes agréés/internes, sessions, inscriptions, pointage présence, attestations officielles A4, verrou probatoire, passerelle RH-03, tableau de bord TPA, exports Excel 4 feuilles) |
| RH-08 | Gestion des temps & rotations (équipes et cycles, planning jour par jour, jours fériés, arrêté de période, import pointages/grille Excel, décompte hebdomadaire en 5 tranches, transferts HS/absences/paniers vers la paie, barème HS daté, export Excel planning + récapitulatif) — **livré avant RH-06/RH-07 à la demande du client** |
| RH-05 | Recrutement & candidatures (expression de besoin avec motifs et quota, vivier de candidats, candidatures multi-étapes, entretiens et notations, intégration directe et atomique en fiche salarié SAL-01 avec affectation de poste RH-01, garde-fou anti-sur-recrutement, verrou probatoire inviolable, lettre d'embauche officielle A4 conforme Loi n°028-2008/AN, bilan consolidé ClosedXML 4 feuilles) |
| RH-06 | Discipline & procédures (procédure de la constatation à la notification, contrôles de procédure en direct, catalogue des sanctions et délais paramétrables, salarié protégé / inspection du travail, 5 lettres PDF, pièces, échéancier, registre des sanctions Excel, mise à pied → absence PAI-04, licenciement → SAL-05) |
| RH-07 | Santé & sécurité au travail (visites médicales et échéances, périodicité par poste, situation médicale par salarié ; EPI : catalogue, remises, renouvellements, fiche de dotation PDF ; accidents du travail : déclaration CNSS + inspection en 48 h ouvrables, fiche PDF pour l'imprimé CNSS, arrêt → absence PAI-04, indemnités journalières 2/3 ; échéancier ; registre Excel 5 feuilles) |

### Prochain

**Phase 4 — Pilotage, automatisation et conventionnel : achevée** avec RH-07 (19 octobre 2026).
Ordre de la feuille de route (« Feuille de route Sigma Paie & RH.pdf », 53 modules) : SAL-05, PAI-03, PAI-08, DEC-05,
PIL-01, RH-01 à RH-08 sont tous livrés. Restent les modules hors phase (21). Ordre proposé au client le 19 octobre 2026 :
**PAI-10 avant la paie de décembre**, puis SOC-04, DEC-07, SAL-02 / SAL-03 / SAL-04, les états (ETA), le pilotage (PIL) et
l'administration (ADM) — à confirmer avec lui avant d'attaquer.

---

## 3. Schéma de base

`SQLiteService.SchemaCible` vaut **38** (constante publique, lue aussi par `VersionApplication`). Chaque montée de
version fait une sauvegarde `.avant-vN.bak` à côté du fichier avant de migrer.

**Mise à niveau tout ou rien (depuis le 19 octobre 2026).** `EnsureSchema` ne migre plus le fichier du dossier
lui-même : il copie la base (`<base>.migration.tmp`), applique la chaîne `AppliquerMigrations` sur la copie, contrôle
le résultat (`PRAGMA quick_check` — seulement si la base était saine avant —, niveau atteint, table `Employees`), puis
substitue la copie à l'original par `File.Replace` (réessais si un antivirus ou une connexion retient le fichier). En
cas d'erreur, de coupure ou d'arrêt brutal, le fichier du dossier est intact et la copie est jetée (un reste est nettoyé
à l'ouverture suivante). Une seule transaction englobante était impossible : certaines migrations posent des `PRAGMA`
hors transaction et ouvrent leurs propres transactions. Un **dossier neuf** est créé de la même façon. Une base dont
le niveau dépasse `SchemaCible` lève `BaseTropRecenteException` (fichier non touché) : l'écran de sélection du dossier
prépare la base AVANT d'ouvrir le dossier et affiche le message (programme trop ancien, ou mise à niveau impossible)
au lieu de planter. `SQLiteService.LireVersionFichier(chemin)` lit le niveau d'un fichier sans rien modifier.

| N° | Objet |
|---|---|
| 4 | Barèmes datés |
| 5 | Périodes de paie |
| 6 | Matricule CNSS du salarié |
| 7 | Ventilation CNSS (assiette + 3 branches) |
| 8 | Dossier salarié étendu, personnes à charge |
| 9 | Variables de paie |
| 10 | Apport des variables (retenues diverses, gains variables) |
| 11 | Rubriques de saisie du plan initial |
| 12 | Affichage de la devise |
| 13 | Absences + droit à congé + base de retenue |
| 14 | Réparation des nets à payer restés à zéro |
| 15 | Archivage du bulletin édité (`BulletinArchive`) |
| 16 | Prêts et échéanciers (`Prets`, `EcheancesPret`) |
| 17 | Profils de paie (`ProfilsPaie`, `LignesProfil`, `AffectationsProfil`) |
| 18 | Exonération de TPA par rubrique (`Rubriques.SoumisTPA`) |
| 19 | Assiette de TPA réellement retenue (`TaxCalculations.AssietteTPA`) |
| 20 | Journal d'audit et traçabilité des opérations (`JournalAudit`, index DateAction, Entite, Utilisateur) |
| 21 | Heures supplémentaires (`HeuresSupplementaires`, index unique `(EmployeeId, Annee, Mois)`, valorisations 15%, 30%, 50%, 100%) |
| 22 | Grilles salariales et conventions collectives (`ConventionsCollectives`, `GrillesSalaires`, `TranchesAnciennete`, colonnes `ConventionId` et `Echelon` dans `Employees`, données CCI-BF) |
| 23 | Solde de tout compte et gestion des sorties (`SoldesToutCompte`, index DateSortie/Employe, colonnes `DateSortie`, `MotifDepart`, `StatutSalarie` dans `Employees`) |
| 24 | Saisies-arrêts, cessions et oppositions (`SaisiesOppositions`, `RetenuesSaisiesMensuelles`, index `DossierId`/`EmployeeId`/`Statut`) |
| 25 | Rappels et régularisations de paie (`RappelsPaie`, `RegularisationsIUTSAnnuel`, `DetailsSimulationRetroactive`, index `EmployeeId`/`AnneeImputation`/`MoisImputation`) |
| 26 | Avantages en nature (`AvantagesNature`, module PAI-08) |
| 27 | Rubrique « Avance sur salaire » (`AVANCE`, 4221/4270) — dette n°7 : donne enfin à l'avance sur salaire (PAI-05) un compte distinct de la retenue générique 422100 |
| 28 | Service d'affectation (`Department`) sur la fiche employé et synchronisation du schéma vierge |
| 29 | Organigramme d'entreprise et postes (`UnitesOrganisationnelles`, `Postes`, colonnes `PosteId`, `ManagerMatricule` dans `Employees`) |
| 30 | Fiches de poste et compétences (`Competences`, `PosteCompetences`, 12 colonnes étendues sur `Postes`, pré-remplissage de 10 compétences de référence) |
| 31 | Entretiens & évaluations (`CampagnesEvaluation`, `EntretiensEvaluation`, `EntretienCriteres`, `EntretienObjectifs`, `EntretienBesoinsFormation`, index unique `UX_EntretiensEvaluation_Campagne_Employe`, pas de `CASCADE` sur `EmployeeId`) |
| 32 | Formation professionnelle et contrepartie TPA (`PlansFormation`, `OrganismesFormation`, `FormationsCatalogue`, `SessionsFormation`, `SessionInscriptions`, index unique `UX_SessionInscriptions_Session_Employe`, colonne `SessionFormationId` sur `EntretienBesoinsFormation`, amorçage 2 organismes et 3 actions catalogue) |
| 34 | Gestion des temps & rotations, RH-08 (`BaremesHeuresSup` daté + 2 barèmes livrés, `HeuresSupplementaires.Heures60/Montant60`, `Absences.PeriodeImputation`, `EquipesTravail`, `EquipesMembres`, `PlanningJours` unique `(EmployeeId, DateJour)`, `JoursFeries`, `ParametresTemps`, rubrique `PANIER`) |
| 37 | Santé & sécurité au travail, RH-07 (`ParametresSST` une ligne, `PeriodicitesVisitePoste`, `VisitesMedicales`, `TypesEPI` + 10 EPI courants à 12 mois, `DotationsEPI`, `AccidentsTravail` référence unique `AT-AAAA-NNNN` ; aucune donnée existante transformée) |
| 36 | Correction de dette : plan de paie initial complet posé (INSERT OR IGNORE) dans tout dossier dont le plan ne contient pas `SALBASE` — signature d'un plan jamais amorcé ; registre `MandatsRepresentatifs` (RH-06) |
| 35 | Discipline & procédures, RH-06 (`ParametresDiscipline` une ligne, `TypesSanction` + 5 sanctions livrées AVT/BLA/MAP/LIC/LFL, `ProceduresDisciplinaires`, `PiecesDisciplinaires` ; aucune donnée existante transformée) |
| 33 | Recrutement & gestion des candidatures (`RecrutementBesoins`, `RecrutementCandidats`, `RecrutementCandidatures`, `RecrutementEntretiens`, index unique `UX_RecrutementCandidatures_Besoin_Candidat`, amorçage d'un besoin initial et de deux profils candidats) |
| 38 | Corrections du guide de formation (21 octobre 2026) : `Migration38_CorrectionsPaie` (REMBFRAIS/FRAISMISS hors TPA si encore au réglage d'origine ; rubriques de sortie `INDCONGES`, `INDDEPART`, `INDDIVERS`, `RETSORTIE` ; `Employees.SoldeCongesReprise`, `DateRepriseConges`), `Migration38_SanteSecurite` (`AccidentsTravail.DateDepotIJ`, `MontantRembourseIJ`, `DateRemboursementIJ`), `Migration38_Entretiens` (`EntretiensEvaluation.ValidePar`), `Migration38_Recrutement` (`Employees.TypeContrat`, `RecrutementCandidatures.TypeContratEmbauche`, `RecrutementBesoins.Indemnite*`), `Migration38_FichesDePostes` (`PosteCompetences.NiveauAVerifier`, lignes existantes marquées « à vérifier », jamais converties), `Migration38_ParametresComptables` (table clé/valeur, valeurs initiales = anciens comptes codés en dur), `Migration38_AuditScelle` (`JournalAudit.Empreinte`/`EmpreintePrecedente`, `JournalAuditScellement`, déclencheurs anti-modification/suppression). Toutes idempotentes, aussi appelées par `CreerSchemaComplet`. |

**Bases vierges.** `CreerSchemaComplet` rejoue désormais les migrations qui CRÉENT quelque
chose (12, 13, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 29, 30, 31, 32, 33, 34, 35, 36, 37 — le plan de paie de la 36 en dernier). Un dossier neuf est enregistré directement au niveau cible et
saute toute la chaîne : il naît avec toutes les tables nécessaires (`Absences`, `Prets`,
`ProfilsPaie`, `JournalAudit`, `HeuresSupplementaires`, `ConventionsCollectives`, `SoldesToutCompte`, `SaisiesOppositions`, `UnitesOrganisationnelles`, `Postes`, `Competences`, `PosteCompetences`, `CampagnesEvaluation`, `EntretiensEvaluation`, `EntretienCriteres`, `EntretienObjectifs`, `EntretienBesoinsFormation`, `PlansFormation`, `OrganismesFormation`, `FormationsCatalogue`, `SessionsFormation`, `SessionInscriptions`, `RecrutementBesoins`, `RecrutementCandidats`, `RecrutementCandidatures`, `RecrutementEntretiens`, `TypesSanction`, `ProceduresDisciplinaires`, `PiecesDisciplinaires`, `ParametresDiscipline`, `MandatsRepresentatifs`, `ParametresSST`, `PeriodicitesVisitePoste`, `VisitesMedicales`, `TypesEPI`, `DotationsEPI`, `AccidentsTravail`) et un plan de paie complet.
La migration 27 n'y figure pas : elle ne fait qu'insérer une rubrique, déjà couverte pour un
dossier neuf par `RubriqueService.RubriquesDeSaisie()` (même principe que la migration 11).

---

## 4. Conventions du code

- **Argent** : entiers en centimes (`DbValue.MoneyToDb` / `MoneyFromDb`). Jamais de REAL.
- **Taux** : TEXT invariant (`DecimalToDb`). Un taux n'est pas un montant, il ne s'arrondit
  pas au centime.
- **Dates** : TEXT `yyyy-MM-dd HH:mm:ss`.
- **csproj ancien format** : tout nouveau fichier doit être déclaré à la main
  (`<Compile>`, `<Page>` + `<DependentUpon>` pour les `.xaml.cs`).
- **XAML** : les colonnes de DataGrid sont liées **TwoWay par défaut**. Une colonne qui
  pointe une propriété calculée doit porter `Mode=OneWay`, sinon WPF lève « liaison TwoWay
  sur une propriété en lecture seule ». Même dans une grille `IsReadOnly`.
- **Barres d'outils** : jamais une grille toutes colonnes `Auto` — elle ne se comprime pas
  et pousse le dernier groupe hors de l'écran. Une colonne `*` absorbe la largeur restante.
- Pas d'accent dans les identifiants C#. Pas de propriété portant le nom de son type.
- **Numéro de version** (`Properties/AssemblyInfo.cs`, seul endroit à changer) : `1.<dernière phase livrée>.<SchemaCible>.<révision>`,
  et `AssemblyInformationalVersion` = « numéro du JJ/MM/AAAA », libellé affiché (titre de la fenêtre, barre d'état, À propos,
  écran de sélection du dossier, manifeste des sauvegardes) via `Helpers/VersionApplication`. **À chaque livraison** : nouvelle
  révision (ou nouveau niveau de schéma) et nouvelle date. 26/10/2026 : `1.4.38.2`.

## 5. Méthode de travail

- **Équivalence stricte.** Toute modification du moteur de calcul audité ship avec des
  valeurs par défaut identiques aux littéraux qu'elle remplace : un dossier sans données
  nouvelles produit un résultat inchangé, au centime. L'écran **« Contrôle du moteur »**
  le vérifie en rejouant chaque bulletin avec l'ancienne formule et la nouvelle, sans
  aucune tolérance d'arrondi. C'est la preuve, pas la promesse.
- **`device_bash` fonctionne à nouveau sur ce poste depuis le 6 octobre 2026** (il était
  inutilisable depuis une mise à jour Windows du 8 septembre). Livraison de RH-08 : une seule
  archive déposée puis extraite sur place, sommes de contrôle vérifiées fichier par fichier
  après extraction, originaux sauvegardés dans `Claude outputs\RH-08_sauvegarde_2026-10-19`.
- **Vérification de compilation dans le cloud (depuis RH-08)** : le compilateur Roslyn du SDK
  .NET 8 compile TOUT le projet (liste `<Compile>` du vrai `.csproj`) contre les assemblys de
  référence .NET Framework 4.8 du poste (WPF compris) et les DLL du dossier `packages`, avec
  des talons générés pour le code XAML (`InitializeComponent`, champs `x:Name`). Zéro erreur
  avant livraison. Les services sont aussi exécutés sous Mono sur une vraie base SQLite
  (fournisseur `Mono.Data.Sqlite`), y compris la migration d'un dossier v33 créé par
  l'ANCIEN code : c'est ce qui prouve l'équivalence au centime des mois antérieurs.
- **Vérifier que CHAQUE fichier modifié d'un lot a bien été livré, pas seulement les
  derniers touchés.** Avant DEC-02, `ApportVariables.cs` (les méthodes `AvecPrets` et
  `AvecProfil` ajoutées pour PAI-05/SOC-03) et le branchement de `MainViewModel.cs` sur les
  écrans Prêts/Profils manquaient purement et simplement du poste du client : le projet ne
  compilait plus, et les deux modules étaient invisibles bien que leurs vues, leurs services
  et le `.csproj` soient corrects. Rien ne l'aurait révélé avant que l'utilisateur essaie de
  compiler. D'où deux réflexes désormais systématiques : (1) après chaque
  `device_commit_files` d'un lot, relire au moins la liste des fichiers acceptés dans la
  réponse de l'outil et la comparer à la liste envoyée ; (2) avant de clore un module, faire
  un `grep` des nouvelles méthodes/classes depuis tous leurs points d'appel réels pour
  confirmer qu'elles existent bien côté service ET qu'elles sont bien invoquées côté
  ViewModel — pas seulement déclarées quelque part.
- **Vérification structurelle sans compilateur Visual Studio disponible ici** : `mcs`
  (Mono 6.8) sert à valider la syntaxe et les signatures du dossier `Models/`+`Services/`
  contre un jeu de stubs (`System.Data.SQLite`, `System.Text.Json`, etc., reconstitués à la
  main dans un dossier `/tmp` jetable). Limites connues, sans rapport avec le code de
  l'application : `mcs` interprète mal les fonctions locales C# 7 et les méthodes à retour
  tuple sans `-langversion:latest`, et plante en interne sur certaines machines à états
  async combinant `out var` et un type de substitution personnalisé. Aucune de ces limites
  n'a jamais révélé un vrai bug — seulement des impasses d'outillage contournées dans la
  copie jetable, jamais dans les fichiers livrés.
  **Ce contrôle ne détecte PAS un fichier manquant au `.csproj`** : la liste de fichiers
  compilés par `mcs` est celle qu'on lui donne à la main, pas celle du `.csproj` réel — un
  nouveau fichier tout à fait valide peut donc compiler parfaitement ici et échouer chez le
  client avec `CS0246` (type introuvable) parce que personne ne l'a déclaré. C'est
  exactement ce qui est arrivé à `ViewModels\MoisSelectionnable.cs` (génération de
  bulletins sur plusieurs mois, 13 septembre 2026) : créé, utilisé par
  `BulletinViewModel`/`BulletinBatchViewModel`, jamais ajouté au `.csproj`. Réflexe
  désormais obligatoire pour tout NOUVEAU fichier `.cs`/`.xaml` créé dans la session : un
  `grep` du nom de fichier dans le `.csproj` staged, avant de livrer — pas seulement
  l'inclure dans la copie `mcs`, qui donne une fausse assurance.
- **Validations avant chaque livraison** : XML bien formé, équilibre des accolades et
  parenthèses, existence des clés `StaticResource`, résolution des bindings contre le
  ViewModel et les modèles, `Grid.Row`/`Grid.Column` dans les bornes, commandes de
  navigation existantes, complétude du csproj.
- **Une rangée `Grid` en `Auto` prend toujours toute la hauteur qu'elle demande ; une
  rangée en `"*"` sans `MinHeight` peut être écrasée jusqu'à disparaître si ses voisines
  grossissent.** En ajoutant la section « Génération sur plusieurs mois » (rangée `Auto`)
  à `BulletinView.xaml`, le panneau « Paramètres du bulletin »/« Aperçu » (rangée `"*"`
  sans plancher) s'est retrouvé écrasé à une hauteur quasi nulle, sans barre de défilement
  visible pour le révéler — signalé par le client le 13 septembre 2026 (captures d'écran),
  corrigé le jour même en donnant un `MinHeight` à la rangée `"*"`, en plafonnant la
  nouvelle section à une hauteur fixe, et en l'enveloppant elle-même dans un
  `ScrollViewer`. Un correctif préventif identique (`MinHeight="220"` sur la rangée du
  tableau des employés) a été appliqué par précaution à `BulletinBatchView.xaml`, qui
  partage la même structure. Réflexe désormais à vérifier avant de livrer tout ajout de
  section dans un écran existant : chaque rangée `"*"` du `Grid` parent a-t-elle un
  `MinHeight`, ou la nouvelle section est-elle en hauteur fixe/plafonnée ?
- Le client recompile lui-même dans Visual Studio : aucun build n'est possible ici.

---

## 6. Décisions structurantes

- **Un bulletin est un instantané opposable.** Il est enregistré au moment où il est
  généré, daté du **dernier jour de sa période** (jamais du jour où le calcul tourne), et
  **archivé en clair** (`BulletinArchive`, JSON) pour être rappelé des années plus tard
  tel qu'il a été remis. Le bouton « Rappeler le bulletin enregistré » n'effectue aucun
  recalcul.
- **Un salarié n'a qu'un bulletin par mois.** L'enregistrement remplace celui du même
  salarié pour le même mois — sinon régénérer trois fois déclarerait trois salaires.
- **Les absences non rémunérées se retiennent sur le BRUT**, avant impôt et cotisations :
  une journée non travaillée réduit l'assiette. Base paramétrable par dossier, **1/30e du
  salaire mensuel par défaut** (choix du client), avec jours ouvrables réels et taux
  horaire 173,33 h en variantes.
- **Droit à congé** : 2,5 jours ouvrables par mois de service **révolu** (minimum légal),
  paramétrable pour les conventions plus favorables.
- **Les totaux d'une période ouverte sont recalculés à chaque lecture** ; ceux d'une
  période clôturée restent figés. C'est le sens de la clôture.
- **Un remboursement de prêt se retient sur le NET**, après l'impôt et les cotisations.
  C'est l'inverse exact d'une absence non rémunérée : le salarié a bien perçu la somme — le
  mois où elle lui a été avancée — et elle a été imposée à ce titre. La retenir sur le brut
  lui rendrait un impôt qu'il doit et fausserait l'assiette déclarée à la CNSS.
- **Une échéance de prêt est réputée retenue quand la période de son mois est clôturée.**
  Elle ne porte aucun état « payée » : deux vérités pour le même mois finissent toujours
  par diverger, et une réouverture de période laisserait l'une des deux mentir. Le solde
  restant dû en découle, et suit donc automatiquement les réouvertures.
- **La retenue d'un mois ne peut jamais dépasser ce qui reste dû.** Un échéancier mal saisi
  ou un mois rejoué ne peut pas récupérer plus que la somme avancée. Symétriquement, la
  dernière échéance absorbe l'écart d'arrondi : la somme de l'échéancier égale le montant
  accordé au franc près.
- **Les lignes de remboursement ne passent pas par le plan de paie.** Elles viennent de
  l'échéancier, pas d'une saisie. Leur donner le code d'une rubrique saisissable exposerait
  au doublon — la retenue automatique plus celle qu'un gestionnaire aurait saisie par
  habitude dans les variables du mois.
- **Un profil de paie est un lien vivant, pas une recopie.** Le salarié porte son profil,
  et les rubriques du profil alimentent son bulletin à chaque calcul. Corriger le profil
  corrige tous ceux qui le portent — c'est l'intérêt du module, décision confirmée par le
  client. Contrepartie assumée : une retouche déplace des nets, et l'écran affiche en
  permanence le nombre de salariés concernés avant d'enregistrer.
- **La variable du mois l'emporte sur le profil.** Une rubrique saisie dans les variables
  du mois écarte celle du profil, ligne par ligne. La saisie du mois est une décision
  explicite prise pour ce mois-là ; le profil n'est qu'un défaut. Les additionner ferait
  payer deux fois la même prime. `AvecProfil` doit donc toujours être appelé APRÈS
  `ApportVariables.Depuis` — dans l'autre ordre, le profil écarterait la saisie.
- **Un profil ne porte aucune rubrique du moteur.** Salaire de base, indemnités plafonnées
  de l'article 106, CNSS, IUTS : le moteur audité les lit sur la fiche du salarié. Les
  rubriques `EstSysteme` et celles en mode `Moteur` sont refusées au sélecteur comme
  ignorées au calcul — les porter en profil les compterait deux fois.
- **L'affectation d'un profil est datée et conservée.** Un salarié promu cadre en avril
  garde son profil d'employé sur ses bulletins de janvier à mars. Le retrait d'un profil
  est lui aussi daté, sous la forme d'une affectation à « aucun profil ». Même exigence
  que pour les barèmes de SOC-02 : recalculer un mois utilise ce qui était vrai ce mois-là.
- **La TPA s'exonère rubrique par rubrique, comme l'IUTS et la CNSS.** `Rubriques.SoumisTPA`
  vaut **vrai par défaut** : une rubrique existante ou nouvellement créée reste dans
  l'assiette tant que personne ne l'en exonère explicitement, ce qui laisse inchangée
  l'assiette de tout dossier qui n'a jamais touché ce réglage. `TaxCalculation.AssietteTPA`
  conserve l'assiette réellement appliquée, pour que DEC-03 imprime ce qui a servi au
  calcul plutôt que de le reconstituer et risquer un écart.
- **Les barèmes datés de SOC-02 sont désormais réellement appliqués au calcul.**
  `TaxCalculatorService.AppliquerBareme` existait depuis SOC-02 mais n'était appelé nulle
  part : chaque écran de calcul travaillait encore sur les taux historiques codés en dur,
  quel que soit le barème saisi. DEC-02 branche les quatre écrans qui produisent une paie
  (Bulletin, Bulletins en masse, Calcul d'IUTS, Calcul en lot) sur
  `BaremeService.BaremeApplicableAsync(periode)` avant le calcul, pour que le taux de
  CNSS et de TPA suivent enfin la date de la période plutôt que la date du jour.
- **Le moteur est un service partagé** : un barème appliqué par un écran reste actif tant
  qu'un autre écran n'en applique pas un différent. L'écran « Contrôle du moteur » compare
  le moteur à ses valeurs **historiques**, pas à un barème quelconque — il remet donc le
  moteur à zéro (`AppliquerBareme(null)`) avant de comparer, sans quoi un écart affiché
  pourrait n'être que l'effet d'un barème resté actif depuis l'écran précédent, pas une
  vraie régression.
- **Génération de bulletins sur plusieurs mois, pour un salarié ou en masse.** Demandée
  par le client le 13 septembre 2026. Sur l'écran « Bulletin de paie », un nouveau bloc
  « Génération sur plusieurs mois » exporte directement en PDF, sans passer par l'aperçu à
  l'écran ; sur « Bulletins en masse », la borne « du/au » remplace la période unique. Dans
  les deux cas, une plage cochable liste chaque mois pour permettre d'en écarter un sans
  redéfinir toute la plage. Un mois clôturé n'est **jamais recalculé** : le bulletin déjà
  archivé pour ce salarié et ce mois est rappelé tel quel et exporté ; s'il n'en existe
  aucun (le mois a été clôturé avant qu'un bulletin n'y soit établi), le couple
  salarié/mois est ignoré et signalé au journal plutôt que d'enfreindre la clôture.
  **Sous-dossier par employé dès que plusieurs mois sont sélectionnés** — décision du
  client — pour ne pas mélanger dans un même dossier les bulletins de plusieurs mois d'un
  même salarié. Une plage d'un seul mois sur l'écran « Bulletins en masse » garde
  exactement le comportement d'avant cette extension (fichiers à plat, sans sous-dossier) :
  c'est le cas le plus courant, il ne devait pas changer.
  **Correctif du 13 septembre 2026** : la liste à cocher des mois n'apparaissait au départ
  que si « Au » dépassait « Du » (`AfficherSelectionMois` testait `MoisPlage.Count > 1`) —
  or les deux champs valent `DateTime.Now` par défaut, donc la liste restait invisible tant
  que le client ne pensait pas lui-même à repousser « Au », ce qui s'est lu comme « le
  sélecteur de mois n'existe pas ». Corrigé en l'affichant dès qu'un seul mois est dans la
  plage (`MoisPlage.Count > 0`, ce qui est toujours vrai) sur les deux écrans concernés —
  la sélection individuelle doit être repérable même pour une plage d'un seul mois.
- **Quatre écrans produisent la même paie** — Bulletin, Bulletins en masse, Calcul d'IUTS,
  Calcul en lot — et prennent tous les mêmes entrées : variables du mois et absences de la
  période. Un calcul annuel ou une régularisation en est exclu : ce n'est pas la paie d'un
  mois.
  **Correctif du 15 septembre 2026** : signalé par le client (« les avantages en nature
  n'impactent pas le bulletin »). Le moteur (`ApportVariables.AvecAvantagesNature`,
  `TaxCalculatorService`, `BulletinService`) et l'écran « Bulletin de paie » et la
  génération individuelle des « Bulletins en masse » étaient déjà correctement câblés
  sur PAI-08. Le vrai trou : l'export « Registre de paie » de `BulletinBatchViewModel`
  (`ExporterRegistreAsync`) reconstruisait sa propre chaîne d'apports et avait été oublié
  lors de l'ajout de PAI-08 (avantages en nature) et de PAI-07 (rappels de paie) — son
  propre commentaire dit pourtant « le registre doit porter exactement les mêmes montants
  que les bulletins individuels ». Corrigé en ajoutant `.AvecRappels(...)` et
  `.AvecAvantagesNature(...)` à cette chaîne, avec le chargement correspondant
  (`ChargerRappelsAsync`, `ChargerAvantagesNatureAsync`), pour retrouver l'invariant.
  **Complété le 15 septembre 2026** : « Calcul d'IUTS » et « Calcul en lot » ont ensuite
  été câblés sur ces mêmes quatre modules (dette n°16 résolue, voir §7) — les quatre
  écrans qui produisent la paie sont désormais alignés.
- **Le PDF du bulletin tient sur une page**, quel qu'en soit le contenu : il est composé
  sur une page de 4 000 points puis réduit juste ce qu'il faut pour entrer dans l'A4.
  Réduire le texte plutôt que couper le document — les signatures ne doivent pas se
  détacher du décompte qu'elles approuvent.
- **DEC-03 (déclaration IUTS/TPA) reprend l'architecture de DEC-04**, en plus simple :
  un seul mois par déclaration, pas de choix de périodicité (l'IUTS et la TPA se
  déclarent chaque mois, jamais trimestriellement). L'échéance est fixée au client :
  **le 10 du mois suivant** (`new DateTime(finPeriode.Year, finPeriode.Month, 1)
  .AddMonths(1).AddDays(9)`), affichée sur l'écran et signalée en dépassement.
  **L'IUTS n'est jamais recalculé** : chaque ligne nominative reprend l'IUTS retenu tel
  qu'il a été enregistré sur le bulletin du salarié — reconstituer le barème progressif
  par tranches ferait courir un risque d'écart avec ce qui a réellement été prélevé,
  contraire au principe « la déclaration dit ce que la paie a retenu » déjà appliqué à
  DEC-04. **La TPA, elle, est comparée au barème** (comme la CNSS) parce que c'est un
  taux plat sur une assiette, pas un calcul par tranches — un écart significatif entre
  la TPA enregistrée et la TPA attendue déclenche une anomalie. **Sur l'état nominatif,
  l'IUTS est détaillé par salarié mais la TPA n'apparaît qu'en un seul total
  entreprise** — choix explicite du client : la TPA est une charge patronale, pas une
  retenue individuelle, elle n'a donc pas sa place ligne par ligne dans l'annexe
  nominative.
  **Effet de bord nécessaire** : `TaxCalculation.AssietteTPA` existait comme propriété
  en mémoire depuis DEC-02 mais n'avait **jamais été persistée** en base — `DataService`
  ne l'écrivait ni ne la relisait. Sans cette assiette enregistrée, DEC-03 n'avait rien
  à comparer au barème. Corrigé par la **migration 19** (colonne
  `TaxCalculations.AssietteTPA`) et le branchement de `DataService` sur l'insertion et
  la lecture de l'historique, à l'identique de ce qu'avait fait la migration 7 pour
  `AssietteCnss` (DEC-01/DEC-04). Pour les bulletins enregistrés avant cette migration
  (assiette à zéro), le service de déclaration retombe sur le salaire brut du bulletin
  comme approximation plutôt que d'afficher une assiette nulle absurde.
  **Piège évité avant livraison** : `SchemaCible` (le seuil qui fait sortir la chaîne de
  migration en avance) valait encore 18 après l'ajout du bloc `if (version < 19)` — tout
  dossier déjà en version 18 aurait donc sauté la migration 19 en silence, et DEC-03
  aurait paru fonctionner en test sur un dossier neuf tout en ne recevant jamais
  l'assiette TPA chez le client. `SchemaCible` est désormais à **19** (voir §3).
  **Correction le jour même de la livraison** : la première version affichait une colonne
  « IFU salarié » sur l'état nominatif et signalait « salarié sans IFU personnel » en
  anomalie — sur la foi d'une hypothèse fausse, calquée sur le numéro CNSS de DEC-04. Le
  client a précisé qu'**au Burkina Faso un salarié n'a pas de numéro IFU propre** : l'IFU
  identifie une entreprise ou un contribuable indépendant, pas un employé — seul le
  dossier (l'employeur) en porte un, et c'est ce seul IFU qui reste vérifié (anomalie
  « IFU du dossier non renseigné », inchangée). Corrigé en retirant entièrement la notion
  d'IFU personnel de DEC-03 : la colonne « IFU salarié », la colonne « Observation » qui
  n'existait que pour ça, et le surlignage rouge de chaque ligne en résultaient supprimés
  (`Models/DeclarationIutsTpa.cs`, `Services/DeclarationIutsTpaService.cs`,
  `Services/DeclarationIutsTpaExportService.cs`, `Views/DeclarationIutsTpaView.xaml`) —
  sans quoi l'anomalie se serait déclenchée sur 100 % des salariés de tout dossier, en
  bruit permanent plutôt qu'en signal. L'état nominatif identifie désormais chaque ligne
  par le matricule interne et le nom, comme le fait déjà DEC-04 pour ses colonnes non
  spécifiques à la CNSS. Le champ `Employee.NumeroIfu` de la fiche salarié (SAL-01)
  n'a pas été touché — il reste un champ libre, optionnel, pour le cas rare où il
  s'appliquerait ; ce n'est que son absence qui n'est plus traitée comme un défaut sur
  cette déclaration.

- **ETA-01 — Livre de paie mensuel (13 septembre 2026)** :
  **Restitution stricte des bulletins enregistrés** : comme pour DEC-03 et DEC-04, le livre
  de paie ne recalcule aucun montant. Il lit `TaxCalculations` joint à `Employees`, ainsi
  que le JSON `BulletinArchive` pour extraire les `LignesVariables` (PAI-02).
  **Colonnes dynamiques des variables** : chaque rubrique variable apparue dans au moins un
  bulletin du mois fait l'objet d'une colonne dédiée, ordonnée selon le plan de paie
  (`RubriqueService`). Les montants sont signés (gains positifs, retenues négatives).
  **Zéro migration de schéma** : toutes les données nécessaires existent déjà en base
  depuis les migrations antérieures (PAI-02, PAI-04, SOC-01).
  **Statut « Brouillon »** : si la période n'est pas encore clôturée dans `PeriodesPaie`,
  l'état le signale nettement à l'écran et dans l'export Excel par un badge et un avertissement.
  **Export Excel** : classeur ClosedXML en feuille unique avec mise en page paysage, en-tête
  employeur officiel, tableau croisé, ligne de totaux de contrôle en gras, et la même charte
  graphique sobre et contrastée que les déclarations.
  **Correctifs du 13 septembre 2026** :
  1. *Ressource XAML `AccentBrush` manquante* : `App.xaml` ne déclarait que `AccentBrush2`, provoquant
     un `XamlParseException` à l'ouverture de `LivrePaieView`. Corrigé en déclarant les deux clés dans `App.xaml`
     et en harmonisant la vue.
  2. *Colonne SQL inexistante `c.RetenueAbsences`* : la table `TaxCalculations` ne possède pas de colonne
     `RetenueAbsences` (la retenue est portée par le bulletin édité dans `BulletinArchive`). La requête SQL
     échouait avec `SQL logic error: no such column: c.RetenueAbsences`. Corrigé dans `LivrePaieService.cs` en retirant
     la colonne de la requête SQL, en lisant `RetenueAbsences` depuis le JSON `BulletinArchive`, et en ajoutant
     les indemnités contractuelles de `Employees` en valeur de repli si l'archive est absente.

- **CPT-01 — Écritures de paie SYSCOHADA (13 septembre 2026)** :
  **Pièce d'OD mensuelle de paie** : centralisation des charges et des dettes de paie du mois sous le
  journal `OD` (`OD-PAIE-AAAA-MM`).
  **Comptes SYSCOHADA révisé** :
  - *Débit (Charges)* : 6611 (salaires de base contractuels), 6631 (logement), 6632 (transport), 6633 (fonction),
    6634 (sujétion), comptes paramétrés sur les rubriques variables de gains (ex. 6612, 6613, 6618),
    6641 (cotisations patronales CNSS), 6413 (taxe patronale et d'apprentissage TPA).
  - *Crédit (Tiers, État et Atténuation de charges)* : 
    - 6611 (ou compte dédié / sous-compte) : retenues pour absences et congés non rémunérés (atténuation directe de charge),
    - 4311 (sécurité sociale CNSS globale : salariale + patronale), 4472 (IUTS),
    - 4473 (TPA due), 4478 (retenue spéciale), 4221 (avances/prêts/retenues sur net), 4220 (salaires nets à payer).
  **Prise en compte des congés et absences non rémunérés (Correctif)** :
  - La retenue pour absences non rémunérées (PAI-04) réduisant le salaire brut en amont de l'impôt et des cotisations,
    l'omettre créait un déséquilibre comptable Débit - Crédit égal au total des absences du mois.
  - Résolu en agrégeant `bulletin.RetenueAbsences` et en insérant la ligne de crédit d'atténuation sur le compte `661100`
    (ou compte de rubrique `ABS` paramétré).
  **Équilibre parfait (Débit = Crédit)** : garanti au centime près par construction, avec correspondance exacte 1-à-1
  entre les totaux du Livre de paie (colonne « Ret. absences » incluse) et les lignes de l'OD comptable.
  **Double export** : classeur Excel ClosedXML avec en-tête officiel, totaux et formatage soigné, ainsi
  que fichier texte délimité standard pour import direct dans tout logiciel de comptabilité (Sage 100, etc.).

- **CPT-02 — Ordres de virement bancaire et récapitulatif des règlements (13 septembre 2026)** :
  **État des paiements des salaires** : génération mensuelle de l'ordre de virement bancaire et de l'état
  récapitulatif des règlements ventilé par mode de paiement (Virements bancaires, Chèques, Espèces, Mobile Money).
  **Réconciliation stricte 1-à-1** :
  - La somme des montants nets des bénéficiaires correspond au centime près au Net à payer du Livre de paie (ETA-01)
    et au compte créditeur 422000 (« Personnel, rémunérations dues ») de la pièce d'OD SYSCOHADA (CPT-01).
  **Filtrage multicritère** :
  - Sélection par banque domiciliataire cible (génération d'un ordre individuel pour une banque donnée, ex. BOA, Coris, Ecobank)
    ou consolidation « Toutes les banques ».
  - Filtrage par mode de règlement (permet d'isoler l'état des chèques pour signature ou la liste de paiement en espèces à la caisse).
  **Détection et contrôle des anomalies RIB** :
  - Détection automatique et signalement immédiat des salariés payés par virement bancaire dont le RIB / numéro de compte
    est manquant ou incomplet, avec avertissement visuel et cartouche d'alerte.
  **Double export automatisé** :
  - *Export Excel ClosedXML* (`.xlsx`) : document officiel prêt pour la banque et la direction, incluant la feuille
    « Ordre de virement » avec cartouche employeur, coordonnées bancaires, tableau des salariés avec montants en FCFA,
    totaux en chiffres et bloc des signataires autorisés (« Le Directeur Général », « Le Responsable Financier »),
    ainsi que la feuille « Récapitulatif règlements » avec pourcentages et effectifs par mode.
  - *Export Fichier de remise bancaire* (`.csv` / délimité standard point-virgule) : fichier normalisé prêt pour
    l'intégration dans les plateformes de web-banking et transmission aux banques.

- **ADM-02 — Journal d'audit et traçabilité des opérations (13 septembre 2026)** :
  **Traçabilité médico-légale des écritures et modifications de paie** : journalisation immuable
  dans `JournalAudit` de toutes les opérations sensibles (ajustement de salaires de base, primes,
  avances, suppressions et clôtures).
  **Indexation optimisée** : index sur `DateAction`, `(Entite, EntiteId)` et `Utilisateur` pour
  des recherches instantanées même après plusieurs années d'exploitation.
  **Reconstitution avant/après** : mémorisation des anciennes et nouvelles valeurs pour un
  contrôle précis lors des contrôles fiscaux ou audits d'entreprise.
  **Export d'audit** : export Excel ClosedXML et CSV filtrable par plage de dates, utilisateur et entité.

- **PAI-03 — Heures supplémentaires et majorations légales (13 septembre 2026)** :
  **Barème légal burkinabè** : gestion des quatre tranches de majoration du Code du travail :
  - 115 % (majorations 15 % pour les 8 premières heures de jour),
  - 130 % (majorations 30 % pour les heures au-delà de la 8ᵉ de jour),
  - 150 % (majorations 50 % pour les heures de nuit en jour ouvrable ou de jour les dimanches/fériés),
  - 200 % (majorations 100 % pour les heures de nuit les dimanches et jours fériés).
  **Taux horaire de base** : calculé sur la base de 173,33 heures mensuelles (`BaseSalary / 173.33m`).
  **Intégration au bulletin** : insertion automatique sous la rubrique `HSUP` lors de la génération
  individuelle ou en masse, avec préséance de la saisie manuelle si déjà saisie.

- **SOC-05 — Grilles de salaires et conventions collectives (13 septembre 2026)** :
  **Classification catégorielle et échelons** : association de chaque salarié à une convention
  collective (`ConventionsCollectives`), une catégorie et un échelon (`GrillesSalaires`).
  **Salaires minima conventionnels garantis** : contrôle en temps réel sur la fiche salarié, avec
  alerte visuelle immédiate si le salaire de base est inférieur au minimum garanti par la convention.
  **Prime d'ancienneté légale (Art. 37 CCI-BF)** :
  - Déclenchement à partir de 2 ans d'ancienneté (5%), puis +1% par année révolue jusqu'à 25 ans (28% max).
  - Base de calcul paramétrable : minimum conventionnel de la catégorie/échelon (`MinimumConventionnel`, défaut)
    ou salaire de base réel contractuel (`SalaireDeBaseReel`).
  - Intégration automatique au bulletin de paie sous la rubrique `PRIMEANC`.
  - Priorité à la saisie manuelle dans les variables de paie (`VariablePaie`) pour éviter tout doublon.

- **SAL-05 — Solde de tout compte et gestion des départs (13 septembre 2026)** :
  **Conformité légale burkinabè (Loi 028-2008/AN & CCI-BF Art. 30, 35, 37)** :
  - *Salaire de présence du dernier mois* : calcul au prorata des jours de présence réelle (`Salaire / 30 × JoursPresence`).
  - *Indemnité compensatrice de congés payés* : reprise automatique du solde de congés acquis (`AbsenceService.ObtenirSoldeAsync`)
    ou saisie manuelle libre, valorisée selon la règle légale du 1/12ᵉ ou du maintien de salaire.
  - *Indemnité compensatrice de préavis (Art. 30 CCI-BF)* : 3 mois obligatoires pour cadres/assimilés, 1 mois (<5 ans)
    ou 2 mois (>=5 ans) pour les ouvriers et employés non-cadres. Non due en cas de préavis effectué ou de faute lourde.
  - *Indemnité légale de licenciement (Art. 35 CCI-BF)* : calcul par tranches d'ancienneté sur le salaire moyen des 12 derniers mois :
    25 % par an (1 à 5 ans), 30 % par an (6 à 10 ans), 40 % par an (>10 ans), avec proratisation exacte des mois révolus.
    Réduite à 50 % en cas de départ volontaire à la retraite ; nulle en cas de faute lourde ou de démission.
  - *Prorata 13ᵉ mois / gratification conventionnelle*.
  - *Apurement des avances et prêts* : reprise automatique du capital restant dû via `PretService.ObtenirSoldeRestantAsync`.
  **Régime fiscal et social (Art. 106 Code Général des Impôts)** :
  - L'indemnité légale de licenciement et de départ à la retraite est **strictement exonérée d'IUTS et de cotisations CNSS**.
  - Les éléments ayant le caractère de salaire (dernier mois, congés payés, préavis, gratification) restent imposables et cotisables.
  **Cycle de vie et mise à jour automatique** :
  - Validation du décompte : archivage opposable dans `SoldesToutCompte` et bascule automatique du salarié en statut `Sorti`
    avec date et motif de sortie sur sa fiche (résolution de la dette technique #15).
  - Traçabilité médico-légale dans le journal d'audit (`AuditService`, action `VALIDER_SOLDE_TOUT_COMPTE`).
  **Édition des documents officiels** :
  - *Reçu pour solde de tout compte* : exports PDF iTextSharp et Excel ClosedXML avec formule légale libératoire et forclusion de 2 ans.
  - *Certificat de travail (Art. 67 Code du travail)* : document officiel mentionnant dates d'entrée/sortie et emplois occupés,
    garanti sans aucune mention désobligeante ou préjudiciable.
  - *Attestation de travail / fin de service* : attestation employeur certifiant les états de service.

- **PAI-06 — Saisies-arrêts, cessions sur salaire et oppositions (14 septembre 2026)** :
  **Conformité légale et conventionnelle (Code du travail burkinabè Loi 028-2008/AN Art. 122+ & OHADA AUPSRVE Art. 177+)** :
  - *Assiette de référence légale* : calcul rigoureux sur le salaire net de référence $R_{net} = \text{Brut imposable} - \text{CNSS salariale (5,5\%)} - \text{IUTS}$.
  - *Barème progressif légal par tranches* :
    - De 0 à 25 000 FCFA : 1/20 (5 %)
    - De 25 001 à 50 000 FCFA : 1/10 (10 %)
    - De 50 001 à 100 000 FCFA : 1/5 (20 %)
    - De 100 001 à 150 000 FCFA : 1/4 (25 %)
    - De 150 001 à 200 000 FCFA : 1/3 (33,33 %)
    - De 200 001 à 300 000 FCFA : 2/3 (66,67 %)
    - Au-delà de 300 000 FCFA : 100 % (totalité de la tranche supérieure).
  - *Sanctuaire du SMIG vital* : protection absolue du salaire minimum interprofessionnel garanti (SMIG = 45 000 FCFA). Si le salaire net est inférieur ou égal au SMIG, la quotité ordinaire saisissable est strictement nulle.
  - *Super-privilège de la pension alimentaire* :
    - Prélèvement prioritaire intégral sur la quotité ordinaire.
    - Droit de mordre sur la fraction insaisissable au-delà du SMIG vital si la quotité ordinaire ne suffit pas.
    - Seul le montant du mois courant bénéficie du super-privilège ; les arrérages sont traités comme créance ordinaire.
  - *Ordre légal de collocation des créances et concours* :
    1. Pension alimentaire légale (terme mensuel courant)
    2. Avis à Tiers Détenteur (ATD / Trésor Public / DGI)
    3. Saisies-arrêts judiciaires (actes d'huissier, greffes)
    4. Cessions volontaires sur salaire (prêts bancaires, délégations)
    5. Oppositions diverses et saisies conservatoires.
    - *Répartition proportionnelle au marc le franc* : en cas de concours de créances de même rang dépassant la quotité disponible, la retenue est ventilée proportionnellement au montant des échéances de chaque créancier.
  - *Cycle de vie et apurement automatique* :
    - Plafonnement automatique de la retenue au solde restant dû (`SoldeRestantDu`).
    - Dès que le solde tombe à zéro, la créance bascule automatiquement en statut `Solde` et les retenues s'arrêtent.
    - Gestion des mainlevées judiciaires ou transactionnelles (`Mainlevee`).
  - *Intégration transparente au moteur de paie* :
    - Connecté via `ApportVariables.AvecSaisies(ApportSaisies)` sur le bulletin individuel (`BulletinViewModel`) et la génération en masse (`BulletinBatchViewModel`).
    - Enregistrement historisé et auditable des retenues mensuelles dans `RetenuesSaisiesMensuelles`.
  - *IHM ergonomique et simulateur de quotité* :
    - Écran WPF avec recherche instantanée, filtres par statut et type de créance, formulaire complet avec validation.
    - Simulateur interactif de quotité saisissable en temps réel avec décomposition détaillée des 7 tranches.
  - *Double export Excel ClosedXML* :
    - *Bordereau mensuel de reversement aux créanciers* : récapitulatif prêt pour la comptabilité, les huissiers et le greffe avec coordonnées bancaires des tiers bénéficiaires.
    - *Registre général des créances* : état complet de suivi des dossiers avec capital d'origine, total retenu, reste dû et situation d'apurement.
- **PAI-07 — Rappels & régularisations de paie (15 septembre 2026)** :
  - *Inviolabilité absolue des périodes clôturées* : aucun déverrouillage de mois clos n'est requis ni toléré. Tout correctif ou revalorisation rétroactive s'impute sous forme de gain ou de retenue sur la période ouverte courante.
  - *Typologie complète des régularisations* : Rappel de salaire rétroactif, rappel de prime/indemnité, rattrapage d'heures supplémentaires omises, trop-perçu sur salaire antérieur (sens Retenue), régularisation annuelle d'IUTS (fin d'année).
  - *Assistant de simulation rétroactive* : calcul automatique différentiel mois par mois des augmentations de salaire de base ou primes passées, avec décomposition exacte des cotisations CNSS salariales (5,5%), patronales (16%), TPA (3%) et Net.
  - *Régularisation annuelle d'IUTS (CGI Art. 109)* : comparaison de l'impôt théorique annuel calculé sur le cumul imposable effectif (avec barème progressif et charges familiales) contre les acomptes mensuels prélevés. Détection automatique des compléments d'impôt (débit) ou crédits restitués (gain), avec génération automatique de la ligne de régularisation pour le bulletin de décembre (non soumise à CNSS/IUTS/TPA).
  - *Branchement transparent au moteur de paie* : `ApportVariables.AvecRappels(ApportRappels)` injecté dans `BulletinViewModel` et `BulletinBatchViewModel`.
  - *Double export ClosedXML* : Registre des rappels & régularisations, et état de régularisation annuelle d'IUTS.
  - *IHM ergonomique* : Onglets de gestion, assistant de simulation rétroactive, liquidation annuelle et formulaires de saisie assistés.

- **DEC-05 — État annuel des salaires (DADS) (17 septembre 2026)** :
  **Restitution stricte, cumulée sur l'année** : même principe que ETA-01 (Livre de paie), dont
  `EtatAnnuelSalairesService` reprend directement la lecture de `TaxCalculations` jointe à `Employees`
  et du JSON `BulletinArchive` pour le détail par rubrique (`PAI-02`) — étendu sur les douze mois de
  l'année plutôt qu'un seul. Zéro nouveau champ recalculé : chaque cumul est une somme de ce que les
  bulletins ont déjà retenu.
  **Alimenté DIRECTEMENT par les périodes clôturées (PAI-01)** — c'est la définition même du module,
  pas une option d'affichage : pour chacun des douze mois de l'année demandée, le service interroge
  `PeriodePaieService.LirePeriodeAsync` et n'inclut dans le cumul que les mois dont le statut est
  « Clôturée ». Un mois jamais ouvert ou encore ouvert est explicitement exclu et listé à part
  (`MoisManquants`), plutôt que de laisser croire à un cumul annuel complet qui ne l'est pas.
  **État « Partiel »** : si l'année n'a pas ses douze mois clôturés au moment de l'édition, l'écran et
  l'export Excel le signalent nettement par un badge et un avertissement détaillé (liste des mois
  manquants), sur le même principe que le badge « Brouillon » d'ETA-01.
  **Portée volontairement alignée sur ETA-01 et PAI-01** : comme les totaux de période et le Livre de
  paie, les rappels et régularisations (`IsRegularization`, PAI-07) et les calculs annuels (`IsAnnual`)
  ne sont pas inclus dans ce cumul — l'écran le dit explicitement dans ses avertissements. Une extension
  future pourrait les intégrer séparément si le besoin se confirme.
  **Colonnes dynamiques des rubriques** : comme sur le Livre de paie, chaque rubrique variable apparue
  sur au moins un bulletin d'un mois clôturé de l'année fait l'objet d'une colonne cumulée dédiée,
  ordonnée selon le plan de paie (`RubriqueService`), montants signés (gains positifs, retenues
  négatives).
  **Zéro migration de schéma** : aucune table nouvelle, `SchemaCible` reste à 27 — le module ne fait que
  lire et cumuler des données déjà en base.
  **Export Excel** : classeur ClosedXML en feuille unique (`EtatAnnuelSalairesExportService`), même
  charte graphique que les autres états (DEC-03, DEC-04, ETA-01), avec la liste des mois clôturés/
  manquants dans l'en-tête et une ligne de totaux de contrôle en gras.
  **Écran dédié** : nouvel onglet de navigation « État annuel des salaires (DADS) » (groupe
  DÉCLARATIONS), sélecteur d'année seul (pas de mois, contrairement à ETA-01 — c'est un état annuel),
  bandeau de synthèse et tableau croisé salariés × rubriques.

- **Correctif — bouton de menu manquant sur DEC-05 (17 septembre 2026)** :
  Signalé par le client (« je ne vois pas État annuel des salaires dans la barre de menu ») juste après
  la livraison de DEC-05 ci-dessus. **Cause** : la barre de navigation n'est PAS générée dynamiquement
  depuis `_navMenuItems` — cette liste ne sert qu'au filtre de recherche du menu. Chaque bouton est
  déclaré à la main dans `MainWindow.xaml`, avec sa propre liaison `Visibility="{Binding
  IsNavXxxVisible}"` vers une propriété calculée dédiée dans `MainViewModel.cs`. La livraison de DEC-05
  avait bien ajouté l'entrée dans `_navMenuItems` et le `DataTemplate` de la vue, mais avait omis les
  deux pièces qui rendent le bouton réellement visible : la propriété `IsNavEtatAnnuelSalairesVisible`
  et le `<Button>` lui-même. **Correctif** : les deux ajoutés, calqués sur le bouton « Livre de paie ».
  **Leçon retenue pour la suite** : toute nouvelle entrée de menu exige ces cinq pièces, pas seulement
  les deux premières — `_navMenuItems.Add(...)` (recherche), `DataTemplate` (rendu), propriété
  `IsNavXxxVisible` (visibilité du bouton), le `<Button>` XAML lui-même, et sa commande `NavigateToXxx`
  câblée dans `MainViewModel.cs` — appliquée scrupuleusement pour PIL-01 ci-dessous.

- **PIL-01 — Tableau de bord masse salariale (17 septembre 2026)** :
  **Évolution mensuelle + double ventilation** : `TableauBordMasseSalarialeService` reprend le principe
  de lecture de ETA-01/DEC-05 (`TaxCalculations` joint à `Employees`, mois clôturés uniquement via
  `PeriodePaieService`, exclusion `IsAnnual`/`IsRegularization`), mais agrège selon TROIS axes en une
  seule passe : par mois (évolution), par catégorie et par service — ces deux derniers cumulés sur toute
  l'année choisie plutôt que mois par mois, pour rester lisible. « Coût employeur » est la colonne
  `MasseSalariale` de `TaxCalculations` (brut + charges patronales), la même grandeur que
  `PeriodePaie.CoutTotalEmployeur` / `TotalMasseSalariale`, simplement nommée d'après ce que l'écran
  affiche.
  **Champ Service ajouté à la fiche salarié** : absent du modèle jusqu'ici (seule la catégorie existait),
  demandé explicitement par le client pour permettre cette ventilation. Nouvelle colonne `Employees.Service`
  (texte libre, ex. « Comptabilité », « Production »), champ éditable sur l'écran Gestion des employés,
  colonne dans la grille, recherche textuelle, et import CSV (colonne `Service` ajoutée au modèle
  `EmployeeCsvService`). Un salarié sans service renseigné apparaît sous « Non renseigné » dans la
  ventilation plutôt que d'être silencieusement omis.
  **Migration de schéma 28** : ajoute `Employees.Service`, et corrige au passage un oubli distinct
  découvert en touchant `CreerSchemaComplet` — `DateSortie`/`MotifDepart`/`StatutSalarie` (ajoutés par la
  migration 23 pour les dossiers existants) n'avaient jamais été reportés dans le schéma complet d'un
  dossier tout neuf, qui se serait donc retrouvé sans ces trois colonnes à la première lecture de sa
  liste de salariés (erreur SQL « no such column »). Sans conséquence pour un dossier déjà migré.
  **Graphique sans dépendance externe** : aucune bibliothèque de graphiques dans le projet ; les barres
  de l'évolution mensuelle et les barres proportionnelles des ventilations sont de simples `Rectangle`/
  `Border` WPF dont la hauteur/largeur en pixels est PRÉCALCULÉE par le service (qui connaît le maximum
  de la série) plutôt que par un convertisseur XAML — même logique que les autres propriétés d'affichage
  déjà précalculées du projet (`EstComplet`, `MoisClosLibelle`, etc.).
  **État « Partiel »** : même bandeau d'avertissement que DEC-05 si les douze mois de l'année ne sont pas
  tous clôturés.
  **Export Excel** : classeur ClosedXML à trois feuilles (`TableauBordMasseSalarialeExportService`) —
  Évolution mensuelle, Par catégorie, Par service — même charte graphique que les autres états.
  **Écran dédié, nouveau groupe de menu** : onglet « Tableau de bord masse salariale » sous un nouveau
  groupe **PILOTAGE** (jusqu'ici absent — ETA-01/DEC-05/CPT-01/CPT-02 étaient tous rangés sous
  DÉCLARATIONS ; un tableau de bord n'est pas une déclaration, d'où un groupe dédié pour ce premier
  module du domaine PIL). Sélecteur d'année seul, sur le même schéma que DEC-05.

---

## 7. Dette connue

À traiter, documentée dans le code aux endroits concernés :

1. ~~`employee.PensionContribution = calculation.CnssEmployee` — le moteur **mute la fiche
   du salarié** pendant le calcul. Report historique conservé parce que le déloger
   changerait la base imposable (article 111). À reprendre avec précaution.~~
   **Résolu le 15 septembre 2026.** En creusant : le champ n'est affiché ni modifiable sur
   AUCUN écran, et chaque écran charge sa propre copie des salariés (`LoadEmployeesAsync` à
   chaque ouverture) — la mutation ne fuyait donc jamais vers une fiche enregistrée par un
   autre écran. Son seul usage réel : `TaxCalculatorService` écrivait la retenue CNSS dans
   `employee.PensionContribution` puis la relisait DEUX LIGNES PLUS BAS pour plafonner la
   déduction retraite de l'article 111 (8 % du salaire de base) — une façon de se repasser
   une valeur à soi-même, dans la même méthode. Remplacé par une variable locale
   (`cnssEmployeePourDeductionRetraite`) : résultat rigoureusement identique (vérifié
   qu'aucun autre code ne lisait ce champ entre-temps), mais le moteur ne modifie plus
   l'objet `Employee` qu'on lui passe.
2. ~~Les **plafonds de l'article 106** (logement 20 %/75 000, transport 5 %/30 000, fonction
   5 %/50 000) et les abattements professionnels viennent encore de `TaxDataService`, pas
   des barèmes datés de SOC-02.~~
   **Résolu le 15 septembre 2026.** `ParametresSociaux` porte désormais huit champs de plus
   (`TauxMaxIndemniteLogement`/`PlafondIndemniteLogement`, `...Fonction`, `...Transport`,
   `AbattementCategoriesSuperieures`, `AbattementAutresCategories`), sur le même principe déjà
   utilisé pour les taux CNSS/TPA : `Historiques()` leur donne les valeurs exactement telles
   qu'elles étaient écrites en dur (les champs correspondants existaient déjà, inutilisés, sur
   `BaremeLegal`), et `DepuisBareme(...)` bascule vers le barème daté seulement si celui-ci
   renseigne une valeur non nulle. `TaxCalculatorService.CalculateProfessionalDeduction` et
   `CalculateExemptAmount` lisent maintenant `ParametresAppliques` au lieu de
   `TaxDataService.ConstantValues`. Le contrôle d'équivalence (DEC-01) reste inchangé : il
   réinitialise `ParametresAppliques` à `Historiques()` avant de comparer, qui vaut
   exactement l'ancien littéral. `EstimateBaseSalaryWithConstraints`, qui sert les
   simulateurs « Estimation du salaire »/« Estimation en lot » (dette n°5, pas une vraie
   dette), continue volontairement de lire `TaxDataService.ConstantValues`.
3. ~~La **migration 7** n'a pas rétro-alimenté la ventilation CNSS des bulletins antérieurs.~~
   **Script de rattrapage écrit le 15 septembre 2026 — PAS ENCORE EXÉCUTÉ.** Choix du
   client : écrire le script, pas le lancer sans un feu vert explicite séparé — il touche des
   données de paie déjà déclarées. `Services/RattrapageCnssService.cs` (ajouté au `.csproj`,
   mais appelé depuis AUCUN écran ni AUCUNE migration automatique) : `AnalyserAsync()`
   (lecture seule) identifie les lignes de `TaxCalculations` où `CnssPartPatronale > 0` mais
   les quatre colonnes de ventilation valent encore 0 — la signature exacte d'une ligne
   jamais ventilée, forcément antérieure aux barèmes datés (DEC-01/DEC-02), donc calculée au
   seul taux patronal historique (16 % pile). L'assiette s'en déduit par simple division
   (`CnssPartPatronale / 0,16`), avec un second calcul de contrôle indépendant à partir de la
   part salariale déjà en base et jamais touchée (`CnssEmployee / 0,055`) : une ligne où les
   deux assiettes ne concordent pas (à 1 FCFA près) est signalée à part comme « suspecte » et
   n'est PAS rattrapée automatiquement. `AppliquerAsync()` dépose d'abord une copie de
   sauvegarde du fichier (même principe qu'avant une migration de schéma), puis écrit les
   quatre colonnes dans une seule transaction.
   **Point d'entrée ajouté le 17 septembre 2026, dans le cadre de l'apurement de la dette
   technique avant reprise de la feuille de route.** Choix du client : un bouton temporaire
   plutôt qu'un lancement automatique. Nouvel onglet « Maintenance » sur l'écran
   « Paramètres » (`SettingsView.xaml`/`SettingsViewModel.cs`), branché sur une instance
   dédiée de `RattrapageCnssService` (`MainViewModel.cs`, même schéma qu'ailleurs : une
   connexion `SQLiteService` par service). Bouton « Analyser » (lecture seule) affiche le
   nombre de lignes à rattraper et de lignes suspectes, avec le détail de ces dernières.
   Bouton « Appliquer... » ne s'active qu'après une analyse ayant trouvé des lignes, redemande
   confirmation (rappel qu'il touche des données de paie déjà déclarées) avant d'écrire, et se
   redésactive après exécution. Reste à faire : l'utilisateur doit lui-même ouvrir
   « Paramètres » → « Maintenance » → « Analyser » sur le dossier réel, relire le rapport, puis
   décider séparément de cliquer sur « Appliquer... ». Ce panneau est explicitement temporaire
   (destiné à être retiré une fois le rattrapage exécuté sur tous les dossiers concernés), pas
   une fonctionnalité permanente de l'application.
4. Les **cumuls annuels** du bulletin sont *estimés* (rémunération du mois × mois écoulés),
   faute d'un historique mensuel complet. Le bulletin l'indique.
5. Les écrans **« Estimation du salaire »** et **« Estimation en lot »** restent des
   simulateurs : ils n'écrivent rien dans l'historique. C'est voulu.
6. Le **numéro d'employeur CNSS** du dossier doit être renseigné, sans quoi la Caisse
   rejette le bordereau. L'écran le signale.
7. ~~Les lignes de remboursement de prêt ne portent **aucun compte général**. CPT-01 devra
   leur en attribuer un — la contrepartie SYSCOHADA d'une avance au personnel — et ce
   choix doit être arrêté avec le comptable, pas deviné ici.~~
   **Résolu le 15 septembre 2026, en deux temps.** D'abord vérifié : « Acompte », « Prêt »,
   « Opposition » et « Cotisation syndicale » avaient chacune, depuis la migration 11, un
   `CompteContrepartie` différent (4271/4272/4273/4274) — mais `EcriturePaieService`
   (l'écriture CPT-01) ne lisait jamais ce champ pour les retenues, seulement
   `CompteComptable`, identique (4221) pour les quatre : dans l'écriture réellement générée,
   toutes les retenues d'un mois se fondaient sur UNE SEULE ligne de crédit 4221, sous le
   libellé de la première rencontrée. Corrigé : `EcriturePaieService` crédite désormais
   `CompteContrepartie` pour une ligne de retenue (repli sur `CompteComptable` puis sur
   422100 si absent), tandis qu'un gain continue de débiter `CompteComptable` comme avant
   (son crédit se règle globalement, en une seule ligne « Personnel, rémunérations dues »,
   422000). Ensuite complété : le type « Avance sur salaire » de PAI-05 (distinct de
   l'acompte, code retenue `AVANCE`) n'avait aucune rubrique correspondante dans le plan de
   paie et tombait donc dans le compte générique 422100 — ajout d'une rubrique `AVANCE`
   (4221/4270, ce dernier une proposition modifiable depuis l'écran « Plan de paie ») dans
   `RubriqueService.RubriquesDeSaisie()` pour les nouveaux dossiers, et migration 27
   (`INSERT OR IGNORE`, schéma porté à 27) pour rattraper les dossiers déjà en service.
8. ~~La **quotité cessible et saisissable** du salaire n'est pas contrôlée~~
   **Résolu le 14 septembre 2026 par PAI-06** : calcul rigoureux par tranches progressives de la quotité
   saisissable et cessible sur le salaire net de référence ($R_{net} = \text{Brut} - \text{CNSS 5,5\%} - \text{IUTS}$),
   sanctuaire vital du SMIG (45 000 FCFA), super-privilège alimentaire et répartition au marc le franc.
9. ~~Les dossiers qui géraient leurs remboursements par une **retenue saisie à la main** dans
   les variables du mois doivent la supprimer en basculant sur ce module, sinon la retenue
   sera opérée deux fois. Rien ne le détecte automatiquement.~~
   **Résolu (détection heuristique) le 15 septembre 2026.** `ApportVariables.AvecPrets`
   compare désormais chaque retenue déjà saisie dans les variables du mois à des mots-clés
   (« PRET », « PRÊT », « AVANCE », « ACOMPTE », « REMB », dans le code ou le libellé) et
   consigne un avertissement dans une nouvelle liste `Avertissements` quand un doublon
   possible se dessine. Comme les remboursements automatiques de PAI-05 portent des codes
   fixes (`PRET`/`AVANCE`/`ACOMPTE`) non passés par le plan de paie, il n'existe pas de
   collision de code exacte à détecter (contrairement au cas PRIMEANC) — d'où l'heuristique.
   Non bloquant, sur les quatre écrans : message ajouté au bulletin (Bulletin, Calcul
   d'IUTS), compteur et journal détaillé (Bulletins en masse, Calcul en lot).
10. ~~Une ligne de profil dont la rubrique est en mode **« base × taux »** n'est pas
    valorisée : résoudre une base qui est elle-même une autre rubrique relève du moteur.
    Ces lignes sont signalées comme « non valorisées » sur l'écran des profils.~~
    **Résolu le 15 septembre 2026.** `ProfilPaieService.Valoriser` résout désormais la base
    par trois voies, dans cet ordre : une rubrique SYSTÈME (salaire de base, une des quatre
    indemnités) lue directement sur la fiche ; une autre ligne du MÊME profil, à condition
    qu'elle ait déjà été valorisée plus haut dans l'ordre d'affichage (`ApportsAsync`
    accumule les montants résolus au fil de sa boucle) ; à défaut de code, le salaire brut
    de la fiche (base + quatre indemnités — l'équivalent de l'ancien calcul, qui n'inclut PAS
    les apports du mois saisis à part, faute de pouvoir les connaître sans boucler sur le
    moteur lui-même : portée volontairement limitée, pas un moteur de formules général). Une
    base introuvable (code inconnu, ou pas encore rencontrée dans ce profil) vaut toujours 0,
    comme avant. `Anomalies()` ne signale plus ces lignes comme « non valorisées » quand elles
    ont un taux renseigné, au même titre que le mode « pourcentage du salaire de base ».
11. Le **rattachement à un profil se fait depuis l'écran « Profils de paie »**, pas depuis
    la fiche du salarié. L'écran « Employés » n'affiche donc pas encore le profil porté —
    à reprendre quand SAL-03 (carrière et historique) ouvrira ce volet de la fiche.
12. ~~La **prime d'ancienneté** reste hors profil : elle dépend de tranches d'ancienneté que
    seul SOC-05 (grilles et conventions collectives, phase 4) saura calculer.~~
    **Résolu le 13 septembre 2026 par SOC-05** : gestion complète des grilles salariales,
    du barème CCI-BF Art. 37 (5% à 28%), et apport automatique de la rubrique `PRIMEANC`.
13. ~~`Rubriques.SoumisTPA` n'a pas encore de case à cocher sur l'écran du plan de paie.~~
    **Résolu le 13 septembre 2026, avant DEC-03.** `RubriqueView.xaml` affiche désormais une
    colonne « TPA » (lecture seule) dans la grille et une case « Entre dans l'assiette de la
    taxe patronale et d'apprentissage (TPA) » dans la fiche, au même endroit et sous la même
    forme que les cases IUTS et CNSS déjà existantes. Aucun changement côté modèle ou service :
    `Rubrique.SoumisTPA` et sa persistance (`RubriqueService`) existaient déjà depuis DEC-02,
    seule l'IHM manquait.
14. ~~**Aucun verrou n'empêche de générer et d'enregistrer un bulletin sur une période qui
    n'a jamais été ouverte.** `PeriodePaieService.EstClotureeAsync` ne teste que la
    clôture (`Statut = 1`) ; un mois sans aucune ligne dans `PeriodesPaie` compte comme
    « non clôturé », donc traité comme ouvert par défaut. Les quatre écrans qui produisent
    une paie (Bulletin, Bulletins en masse, Calcul d'IUTS, Calcul en lot) n'appellent
    `PeriodePaieService` à aucun moment — seules la saisie des variables du mois et celle
    des absences vérifient la clôture, jamais l'ouverture. Signalé par le client le
    13 septembre 2026.~~
    **Résolu (avertissement, sans blocage) le 15 septembre 2026.** Choix du client :
    « avertir sans bloquer », plutôt qu'un verrou dur — `PeriodePaieService` a été conçu dès
    l'origine pour que rien ne soit verrouillé tant qu'une période n'a pas été ouverte
    explicitement, et l'exiger aurait bloqué tout calcul dans un dossier n'ayant jamais ouvert
    de période. Les quatre écrans qui produisent une paie appellent désormais
    `PeriodePaieService.LirePeriodeAsync` et ajoutent une mention au message final si la
    période n'a jamais été ouverte, sans empêcher le calcul ni l'enregistrement.
15. ~~**Aucune gestion de sortie d'un employé (fin de contrat, démission).**~~
    **Pris en charge et résolu dans SAL-05 (Solde de tout compte)** : ajout des champs
    `DateSortie`, `MotifDepart`, `StatutSalarie` (Actif / Sorti) dans `Employees` et l'IHM,
    avec calcul des indemnités de rupture et édition des documents légaux.
16. ~~**« Calcul d'IUTS » (`CalculationViewModel`) et « Calcul en lot »
    (`BatchSalaryCalculationViewModel`) ne sont câblés que sur absences, prêts et profil.**~~
    **Résolu le 15 septembre 2026.** Les deux écrans reçoivent maintenant les quatre mêmes
    modules que Bulletin et Bulletins en masse : heures supplémentaires (PAI-03), convention
    collective (SOC-05), rappels et régularisations (PAI-07) et avantages en nature (PAI-08).
    `CalculationViewModel` les ajoute à sa chaîne d'apport mono-salarié
    (`ChargerApportDuMoisAsync`) ; `BatchSalaryCalculationViewModel` les précharge pour tout
    l'effectif (mêmes couples `ChargerXxxAsync`/`XxxDe` que `BulletinBatchViewModel`) avant
    de les appliquer par salarié. Dans `MainViewModel.cs`, la construction de
    `_calculationViewModel` et `_batchSalaryCalculationViewModel` a dû être déplacée après
    celle des services PAI-03/SOC-05/PAI-07/PAI-08 (ils n'existaient pas encore à leur ancien
    point de construction). Les quatre écrans qui produisent la paie (§6) sont maintenant
    alignés sur les mêmes modules.

**Détour clos le 15 septembre 2026.** Sur demande explicite du client, tous les points de
cette section ont été traités un par un (14 → 9 → 2 → 10 → 7 → 3 → 1), par dérogation
assumée à la règle habituelle de l'ordre strict de la feuille de route. Seule la dette n°3
reste concrètement ouverte : le script est écrit, son exécution attend un feu vert séparé.
Les points 6 et 11 n'appelaient aucune action (choix du client). Retour à l'ordre strict de
la feuille de route à partir d'ici — après avoir confirmé le prochain module (§2).

17. ~~**`ConventionId`/`Echelon` (SOC-05) et `DateSortie`/`MotifDepart`/`StatutSalarie`
    (SAL-05) ne survivaient à aucun enregistrement d'employé.**~~
    **Résolu le 16 septembre 2026**, trouvé en préparant le modèle d'import CSV
    (ci-dessous). Même famille de bug que celui déjà réglé pour l'indemnité de sujétion /
    fonction / date d'entrée / situation matrimoniale (voir le commentaire historique sur
    `DataService.ColonnesEmploye`) : SOC-05 et SAL-05 avaient bien ajouté leurs colonnes au
    schéma et leurs champs à `Employee`, et SAL-05 les avait même câblés sur la fiche
    (`ConventionsDisponibles`, `EchelonsDisponibles`, `StatutsSalarieDisponibles` dans
    `EmployeeManagementViewModel`, liés en XAML) — mais `DataService.ColonnesEmploye`,
    `LireEmploye` et `AddEmployeeAsync` (le seul chemin de lecture/écriture réel de la
    table `Employees`) n'avaient jamais été mis à jour : assigner une convention/échelon ou
    enregistrer une sortie ne survivait à aucune fermeture de l'application, en silence.
    Corrigé en ajoutant les cinq colonnes aux trois endroits. `DbValue` gagne au passage
    `IntNullableFromDb`, par symétrie avec les autres lecteurs `...FromDb`.

    **Import CSV des employés (même jour, à la demande du client).** L'écran « Gestion des
    employés » avait déjà un bouton « Importer des employés » et une méthode
    `DataService.ImportEmployeesFromFileAsync`, mais celle-ci ne lisait que du JSON (le code
    portait littéralement le commentaire « adaptez pour CSV si besoin ») et remplaçait
    TOUTE la liste des employés (`DELETE FROM Employees` puis réinsertion) — un comportement
    pensé pour restaurer une sauvegarde complète, dangereux pour un CSV de quelques nouvelles
    recrues. Ajout de `Services/EmployeeCsvService.cs` : lecture d'un CSV (séparateur `;`,
    UTF-8 avec repli Windows-1252, colonnes identifiées par nom d'en-tête plutôt que par
    position) vers des `Employee`, avec erreurs bloquantes (Nom/Prénom manquants, montant
    illisible) et avertissements non bloquants (date illisible, convention introuvable,
    colonne inconnue) reportés ligne par ligne. `ImportEmployeesFromFileAsync` distingue
    maintenant `.csv` (ajout / mise à jour PAR MATRICULE, sans jamais supprimer un employé
    absent du fichier — comportement demandé par le client) et `.json` (comportement
    historique inchangé, remplacement complet). `EmployeeManagementViewModel.ImportEmployeesAsync`
    accepte les deux extensions et affiche le bilan (ajoutés/mis à jour/lignes ignorées),
    avec le détail complet dans une boîte de dialogue dès qu'une erreur ou un avertissement
    existe. Modèle livré au client : `Claude outputs/Modele_Import_Employes.csv` (en-têtes +
    deux lignes d'exemple) et `Modele_Import_Employes_Notes.md` (légende des colonnes).

18. **Signalé le 16 septembre 2026 : « la modification d'une fiche employé (catégorie,
    via réimport CSV) ne persiste pas — c'est toujours l'ancien état qui revient. »**
    Investigation en cours. Écarté : build obsolète (le client a recompilé/relancé sans
    erreur avant de tester) ; `Services/DataService.LoadEmployeesAsync` et
    `BatchSalaryCalculationViewModel.LoadEmployeesAsync` (bouton « Charger les employés »)
    relisent bien la base à chaque clic, sans mise en cache visible. Une première
    hypothèse — un réimport du même fichier sans Matricule créant un DOUBLON à chaque
    fois plutôt que de mettre à jour la fiche existante, celle-ci restant alors invisible
    dans une liste non triée (`BatchSalaryCalculationView` n'a aucun tri par défaut) —
    a été écartée par le client (« une seule ligne, mais avec l'ancienne valeur »). Corrigé
    quand même, par robustesse (le client l'a demandé) : `EmployeeCsvService.Lire` reconnaît
    désormais un employé existant par Nom + Prénom quand le Matricule est vide (nouveau
    paramètre `employesExistants` remplaçant `matriculesExistants` ; en cas d'homonymes,
    la ligne crée un nouvel employé plutôt que de deviner, avec avertissement). En parallèle,
    repéré (non lié au signalement, à vérifier séparément) : le bouton **« Charger un
    employé »** de l'écran « Calcul d'IUTS » (`CalculationViewModel.ExecuteLoadEmployeeAsync`)
    ~~est resté un bouchon de démonstration — il charge toujours `employees[0]`, le premier
    de la liste SANS TRI, jamais un employé choisi ; son propre commentaire l'admet
    (« on simule l'opération »).~~
    **Résolu le 17 septembre 2026, dans le cadre de l'apurement de la dette technique
    avant de reprendre la feuille de route.** Un vrai sélecteur a été ajouté : recherche
    rapide (`FiltreSalarie`) + liste déroulante (`SalariesFiltres`/`SalarieASelectionner`),
    même schéma que sur les écrans Absences/Prêts/Profils, rechargé à chaque activation de
    l'écran (`RefreshSettingsAsync`, déjà appelée par `OnIsActiveChanged`) pour ne jamais
    proposer une fiche périmée — même principe que l'item 21. Le bouton charge désormais le
    salarié réellement choisi, relu fraîchement en base (`GetEmployeeByIdAsync`) plutôt que
    la copie affichée dans le sélecteur, par le même principe anti-péremption que l'item 19.
    ~~Reste à faire : obtenir du client le fichier CSV exact et le message d'import exact
    (bilan + détail des erreurs/avertissements le cas échéant) pour trouver la cause réelle
    de CE signalement, qui n'est toujours pas confirmée.~~
    **Clos le 17 septembre 2026 :** le client confirme que le problème ne se reproduit plus.
    Cause exacte non isolée séparément (le signalement n'avait jamais été confirmé) ;
    persiste éventuellement grâce au correctif de robustesse Nom+Prénom déjà en place
    plus haut dans cet item.

19. **Signalé le 16 septembre 2026 (sans lien avec le CSV cette fois) : « je corrige la
    date d'entrée dans la fiche employé, je relance le calcul du bulletin, l'ancienne date
    s'affiche toujours ; et la correction faite dans la fiche est ensuite automatiquement
    remplacée par l'ancienne date. »** Cause RÉELLE identifiée et corrigée — probablement
    le même mécanisme que l'item 18 (non confirmé pour celui-ci faute du fichier CSV
    demandé). Deux ingrédients :
    (a) `BulletinViewModel` et `BatchSalaryCalculationViewModel` chargent leur propre copie
    de la liste des employés en mémoire — la première une seule fois à l'ouverture de
    l'écran (aucun bouton d'actualisation), la seconde au clic sur « Charger les employés »
    — et ne la relisent jamais automatiquement ensuite : une correction faite entre-temps
    dans la fiche (écran Gestion des employés) ou par import CSV n'apparaît donc pas tant
    qu'on n'a pas rouvert le dossier (Bulletin) ou recliqué sur Charger (Calcul en lot) ;
    (b) `DataService.AddCalculationToHistoryAsync` — déclenché par tout enregistrement de
    bulletin/calcul — réécrit la fiche employé ENTIÈRE (`INSERT OR REPLACE`) avec l'objet
    `Employee` attaché au calcul enregistré. Combinés : générer/enregistrer un bulletin avec
    une copie d'employé périmée ne se contente pas d'afficher l'ancienne valeur, il la
    **réécrit en base par-dessus la correction**, silencieusement — exactement le
    comportement décrit. Corrigé en supprimant la staleness à la source plutôt qu'en
    touchant à `AddCalculationToHistoryAsync` (dont le comportement reste utile pour créer
    la fiche d'un « calcul libre » sur un employé encore jamais enregistré) : `BulletinViewModel.
    GenererBulletinAsync` relit désormais la fiche de l'employé sélectionné directement en
    base (`GetEmployeeByIdAsync`) juste avant de calculer, et `BatchSalaryCalculationViewModel.
    CalculateAllAsync` relit désormais `LoadEmployeesAsync()` au lieu de réutiliser la liste
    `Employees` telle qu'affichée. Ni l'un ni l'autre écran ne permet d'éditer les champs de
    la fiche employé directement à l'écran (pas de binding TwoWay vers l'employé sélectionné
    dans leurs XAML respectifs) : cette relecture ne peut donc écraser aucune saisie en
    cours.
    **Complété le 16 septembre 2026 (suite, sur demande du client « corrige toutes les
    anomalies de ce genre ») — les trois angles morts restants du même item 19 :**
    (c) `BulletinBatchViewModel` (écran « Bulletins en masse ») avait exactement le même
    défaut que `BulletinViewModel` : sa liste `Employees` se charge une seule fois à
    l'ouverture de l'écran, sans aucun bouton d'actualisation, et `GenererEtExporterAsync`
    (génération + enregistrement en masse) comme `ExporterRegistreAsync` (registre de paie)
    calculaient à partir de cette liste figée. Corrigé de la même façon : les deux méthodes
    ne gardent de `Employees` que la SÉLECTION (quels employés sont cochés), et relisent les
    fiches elles-mêmes fraîchement en base (`LoadEmployeesAsync`) juste avant de calculer.
    (d) Filet de sécurité général, au niveau des données plutôt que par écran, pour couvrir
    tout chemin de sauvegarde non encore identifié : `DataService.AddCalculationToHistoryAsync`
    ne réécrit plus la fiche employé à l'aveugle. Si l'employé n'existe pas encore en base
    (calcul libre sur un employé tout juste créé dans un écran de calcul), la fiche est créée
    comme avant. S'il existe déjà, seuls les champs qu'un écran de calcul a effectivement
    vocation à modifier (identité, catégorie, éléments de paie — Prénom, Nom, Catégorie,
    IsHigherCategory, Salaire de base, indemnités, charges familiales, conjoint travaille,
    cotisation retraite) sont recopiés sur la fiche ACTUELLE relue en base ; tout le reste
    (état civil, coordonnées, convention, date de sortie...) vient toujours de cette fiche
    actuelle, jamais d'une copie en mémoire potentiellement périmée. Ce filet rend inoffensif
    tout oubli similaire dans un écran non encore audité.
    (e) `CalculationViewModel.LoadExistingCalculation`/`LoadFromHistory` (recharger un calcul
    depuis l'historique) réutilisait l'instantané `Employee` figé au moment de ce calcul
    passé — si l'utilisateur cliquait ensuite « Enregistrer » sans rien recalculer, ce même
    mécanisme réécrivait la fiche actuelle avec cet instantané historique. Converties en
    `LoadExistingCalculationAsync`/`LoadFromHistoryAsync` (async, seul appelant externe mis à
    jour : `MainViewModel.cs`, l'abonnement à `_historyViewModel.CalculationSelected`) : la
    fiche actuelle est désormais relue en base (`GetEmployeeByIdAsync`) au moment du
    chargement, avec repli sur l'instantané historique seulement si l'employé n'existe plus.
    L'écran « Calcul d'IUTS » reste volontairement un éditeur de fiche (Catégorie, Salaire de
    base, indemnités... restent modifiables directement dans son formulaire, à la différence
    du Bulletin) : cette décision n'a pas été changée, mais elle est maintenant sans risque
    pour les autres champs de la fiche grâce au filet de sécurité (d) ci-dessus.

20. **Signalé le 16 septembre 2026 : « le calcul en lot d'IUTS prend en compte aussi les
    employés sortis, il ne doit tenir compte que des employés actifs. »** Confirmé : ni
    `BatchSalaryCalculationViewModel` (« Calcul en lot ») ni `BulletinBatchViewModel`
    (« Bulletins en masse ») ne filtraient par statut — les deux chargeaient tous les
    employés de `DataService.LoadEmployeesAsync`, sorti ou suspendu compris. Le solde de
    tout compte d'un salarié qui part se traite par l'écran dédié (`SoldeToutCompteViewModel`),
    jamais par un bulletin mensuel ordinaire : un employé « Sorti » ou « Suspendu » n'a donc
    rien à faire dans une paie de masse. Corrigé en utilisant `Employee.EstActif` (propriété
    déjà présente sur le modèle — `StatutSalarie == 0` ET pas encore atteint sa `DateSortie` —
    mais jamais câblée jusqu'ici) comme filtre systématique : `BatchSalaryCalculationViewModel.
    LoadEmployeesAsync` (liste affichée) et `CalculateAllAsync` (liste réellement calculée,
    relue fraîchement depuis l'item 19) ; `BulletinBatchViewModel.LoadEmployeesAsync` (liste
    à cocher) et `GenererEtExporterAsync`/`ExporterRegistreAsync` (liste réellement traitée,
    filtrée une seconde fois au moment du calcul par sécurité, au cas où un statut aurait
    changé entre l'affichage et le lancement). Périmètre volontairement limité aux deux écrans
    de LOT : les sélecteurs d'un seul employé (« Bulletin de paie », « Calcul d'IUTS ») restent
    inchangés, un besoin ponctuel de rappeler/consulter la fiche d'un employé sorti (bulletin
    déjà archivé, régularisation) restant légitime là.

21. **Signalé le 16 septembre 2026 : « quand je modifie une fiche employée ou toute autre
    information, je dois fermer le dossier et le rouvrir pour que ce soit pris en compte. »**
    Confirmé, ce n'est pas normal — c'est la forme générale des items 18-20. Cause : à
    l'exception de « Gestion des employés » (reconstruit à neuf à chaque navigation, donc
    toujours à jour), presque tous les écrans sont des instances UNIQUES créées une seule
    fois par `MainViewModel` à l'ouverture du dossier (voir son constructeur) — leur liste
    d'employés (ou toute autre donnée) n'est chargée qu'à cet instant précis et ne se
    recharge jamais toute seule ensuite. Fermer et rouvrir le dossier reconstruit tout depuis
    zéro, ce qui explique pourquoi ça « corrige » le problème en apparence. Corrigé pour les
    écrans qui chargent la liste des employés (`DataService.LoadEmployeesAsync`), en
    utilisant `BaseViewModel.OnIsActiveChanged` — un point d'extension déjà présent et déjà
    utilisé une fois par `CalculationViewModel` pour ses réglages, mais jamais étendu
    ailleurs : ce hook s'exécute automatiquement à chaque fois qu'un écran redevient actif
    (donc à chaque navigation vers lui, sans action de l'utilisateur). Écrans corrigés,
    chacun rechargeant sa liste d'employés (et, pour Prêts/Profils/Absences, l'ensemble de
    leurs données) au retour sur l'écran : `BulletinViewModel`, `BatchSalaryCalculationViewModel`,
    `BulletinBatchViewModel`, `PretViewModel`, `ProfilPaieViewModel`, `AbsenceViewModel`. Pour
    Bulletin et Bulletins en masse, la sélection de l'utilisateur (employé choisi, cases
    cochées) est explicitement conservée par matricule à travers le rechargement, pour ne
    pas la réinitialiser à chaque retour sur l'écran. `AvantageNatureViewModel` n'a pas eu
    besoin de ce correctif : `MainViewModel` rechargeait déjà ses données à chaque clic sur
    son propre lien de navigation (`NavigateToAvantagesNatureCommand`), tout comme
    `RappelPaieViewModel` et `SaisieOppositionViewModel` pour les leurs — un second mécanisme
    de rafraîchissement déjà présent dans le code, coexistant avec celui-ci. **Volontairement
    non corrigé : `VariablePaieViewModel`** (écran « Variables du mois »). Contrairement aux
    autres, une ligne ajoutée via « Ajouter » n'est PAS enregistrée tout de suite : elle
    reste seulement dans la liste affichée jusqu'au clic sur « Enregistrer ». Recharger
    automatiquement à chaque retour sur l'écran effacerait silencieusement ces lignes non
    encore enregistrées — un risque de perte de saisie pire que le bug signalé. Cet écran
    garde donc son bouton « Charger » manuel uniquement ; à reprendre plus tard si le besoin
    se confirme, avec un vrai suivi des lignes non enregistrées avant d'activer un
    rechargement automatique. Peut rester un point de vigilance sur d'autres écrans non
    encore audités pour cette classe de problème (rubriques, conventions collectives,
    barèmes...) si le client rapporte un cas similaire ailleurs.

22. **Signalé le 16 septembre 2026 : « apparemment, il y a beaucoup de bugs dans le
    logiciel, découvre-les et corrige-les une bonne fois pour toutes. »** Demande volontairement
    large plutôt qu'un symptôme précis : traitée comme un audit complet plutôt qu'une
    correction ponctuelle. Méthode : l'intégralité du code (ViewModels, Services, Models,
    Helpers, Converters — les XAML volontairement exclus, hors périmètre d'un audit de
    logique métier/intégrité des données) relue par cinq analyses indépendantes ciblées
    (couche ViewModels ; Services — session/données ; Services — moteur IUTS ; Services —
    cycle de vie employé ; Services — prêts/registres/exports), chacune avec consigne
    explicite de ne pas re-signaler les items 1-21 déjà connus. Deux pistes remontées ont été
    creusées puis écartées à dessein (voir ci-dessous) ; les anomalies restantes ont été
    corrigées.

    **Corrigé — couche ViewModels (régression et lacunes issues de l'item 21) :**
    (a) `PretViewModel`, `ProfilPaieViewModel` : le rechargement automatique ajouté par
    l'item 21 reclonait `PretEnCours`/`ProfilEnCours` à chaque retour sur l'écran, effaçant
    une modification en cours de saisie non encore enregistrée. Corrigé par un garde-fou
    « même fiche » (comparaison d'`Id`) : le clone n'est refait que si la sélection a
    réellement changé. (b) `SaisieOppositionViewModel` : même défaut sur les champs du
    formulaire (`OnSelectedSaisieChanged`), corrigé en n'appliquant la resynchronisation que
    hors mode édition (`IsEditing`). (c) `MainViewModel.NavigateToEmployeeManagement` ne
    désactivait jamais l'écran quitté (`IsActive = false` manquant, contrairement à
    `NavigateTo`), ce qui empêchait cet écran de se rafraîchir à la prochaine visite —
    corrigé. (d) `AuditViewModel` : le rechargement automatique ne jouait qu'à la toute
    première visite (garde `Entrees.Count == 0` supprimée). (e) `PeriodePaieViewModel`,
    `BaremeViewModel`, `RubriqueViewModel` : ces trois écrans n'avaient jamais reçu le
    rafraîchissement automatique de l'item 21, oubli corrigé (avec le même garde-fou
    « même fiche » que (a) pour éviter toute perte de saisie en cours). (f) `HistoryViewModel` :
    le rafraîchissement (retour sur l'écran, bouton Actualiser, bouton Réinitialiser les
    filtres) bloquait le thread d'interface via un `.Wait()` synchrone sur une méthode async —
    corrigé en appelant directement la version asynchrone.

    **Corrigé — Services et Models :**
    (g) `ConventionCollectiveService.CalculerAnciennete` : seuil de 2 ans codé en dur en plus
    du barème d'ancienneté de la convention, empêchant toute prime pour une convention dont
    la première tranche démarre avant 2 ans — supprimé, le barème se suffit à lui-même.
    (h) `HeureSupplementaireService.ChargerSaisieMoisAsync` : la requête listait TOUS les
    employés sans filtre de statut malgré son commentaire annonçant « les employés actifs » —
    un salarié sorti pouvait recevoir des heures supplémentaires saisies. Filtre ajouté.
    (i) `AvantageNatureService.MapperAvantage` : les dates étaient relues avec la culture du
    poste de travail alors qu'elles sont écrites en culture invariante — un salarié utilisant
    une machine en anglais ou toute culture à format de date différent pouvait faire échouer
    silencieusement la lecture des dates d'avantages en nature. Corrigé en alignant lecture et
    écriture sur `CultureInfo.InvariantCulture`. (j) `SaisieOpposition.EstActive` ignorait
    totalement `DateFin` : une saisie-arrêt à durée déterminée continuait d'être prélevée
    après son terme légal — corrigé. (k) `DataService.SaveCalculationsHistoryAsync` (restauration
    de sauvegarde JSON) appelait à tort le même chemin qu'un enregistrement normal
    (`AddCalculationToHistoryAsync`), qui (i) réécrivait la fiche employé actuelle avec
    d'anciens instantanés historiques arbitraires et (ii) bloquait toute la restauration
    (transaction annulée en entier) dès le premier bulletin d'un mois déjà clôturé —
    c'est-à-dire systématiquement, pour tout dossier ayant un historique réel. Corrigé par un
    chemin d'insertion dédié (`InsererLigneCalculAsync`) qui ne touche ni la fiche employé, ni
    la clôture de période — une restauration de sauvegarde doit reconstituer l'historique tel
    qu'il était, pas le revalider comme une saisie du jour. (l) `DataService.
    DeleteCalculationFromHistoryAsync` n'avait, seule parmi les écritures sur l'historique,
    aucun contrôle de période clôturée : un bulletin déjà déclaré pouvait être supprimé
    silencieusement — corrigé, même verrou que les autres écritures.

    **Corrigé — Services (suite, module par module) :**
    (m) `SaisieOppositionService.AppliquerEtEnregistrerRetenuesMoisAsync` : en cas de
    recalcul d'un mois déjà traité, les saisies-arrêts actives étaient relues AVANT que la
    retenue déjà enregistrée pour ce même mois soit réintégrée à leur solde restant dû — le
    recalcul partait donc d'un solde compris comme s'il avait déjà été prélevé deux fois.
    Corrigé en réordonnant la méthode : réintégration puis nettoyage des lignes du mois
    d'abord, relecture des saisies actives ensuite. (n) `SoldeToutCompteService` : deux
    anomalies liées au solde de tout compte d'un salarié qui part — la dette de prêts en
    cours excluait les prêts « Suspendu » du calcul (corrigé, les deux statuts comptent
    désormais), et surtout, le prêt du salarié n'était jamais effectivement soldé/annulé une
    fois le solde de tout compte enregistré : il restait « En cours » indéfiniment, échéances
    futures comprises, alors que la dette avait déjà été déduite du solde versé. Corrigé en
    appelant `PretService.AnnulerAsync` sur chaque prêt du salarié après l'enregistrement du
    solde (hors transaction, `PretService` ouvrant la sienne propre) ; un échec de ce solde
    automatique ne fait plus échouer le solde de tout compte déjà enregistré, il est
    seulement journalisé pour clôture manuelle depuis l'écran « Prêts et avances ».
    (o) `AbsenceService` : aucun contrôle n'empêchait d'enregistrer, pour un même salarié,
    deux absences dont les dates se chevauchent — un même jour pouvait ainsi être compté deux
    fois dans le compteur de congés et/ou donner lieu à une double retenue sur le brut.
    Corrigé pour la saisie unitaire (`EnregistrerAsync`) et pour l'import/attribution en masse
    (`EnregistrerEnMasseAsync`, chevauchement à la fois entre absences du même lot et avec
    celles déjà en base).

    **Investigué et écarté (pas des bugs) :**
    (p) Un doublon possible de lignes `TaxCalculations` par salarié/période, redouté pour son
    impact sur les écritures de paie/ordres de virement/rappels, a été vérifié dans
    `DataService.EnregistrerBulletinDuMoisAsync` : cette méthode supprime systématiquement
    toute ligne existante pour le même salarié/période avant d'insérer la nouvelle — l'unicité
    est garantie par construction, aucune correction nécessaire.
    (q) Le référentiel daté `BaremeLegal.Tranches`/`ChargesFamille` (barème IUTS et charges de
    famille) n'est toujours pas branché sur `TaxCalculatorService`, qui continue de lire ces
    valeurs depuis les fichiers JSON (`TaxDataService`) ; de même, `RetenueSpeciale` reste à
    1 % en dur. **Ce n'est pas un oubli** : `BaremeLegal.cs` porte une note d'architecture
    explicite indiquant que ce branchement est une étape volontairement séparée, pour ne pas
    modifier le moteur IUTS audité en même temps que le stockage de ses paramètres (seuls
    CNSS/TPA/abattement/plafond, via `ParametresSociaux.DepuisBareme`, ont déjà été branchés
    lors d'un item antérieur). Point non corrigé ici à dessein — un branchement du barème et
    de la retenue spéciale reste à faire, mais comme étape dédiée et testée séparément, pas
    en marge d'un audit général de bugs.

    **Délibérément non traité, priorité plus faible :** absence de contrôle de tri/continuité
    des tranches d'IUTS dans les fichiers JSON de `TaxBrackets` (ces fichiers ne sont pas
    modifiables depuis une saisie utilisateur, risque faible) ; absence de contrôle de
    chevauchement des périodes de rappel de salaire dans `RappelPaieService` (simple
    observation, pas une anomalie confirmée en usage réel). À reprendre si le client rapporte
    un cas concret.

23. **Demandé le 16 septembre 2026 : « branche barème IUTS sur le référentiel daté »** —
    l'étape que l'item 22(q) avait délibérément laissée de côté. `ParametresSociaux` porte
    désormais deux champs de plus, sur le même principe déjà utilisé pour les taux CNSS/TPA
    et les plafonds de l'Art.106 (dette n°2) : `Tranches` (`List<TaxBracket>`) et
    `ChargesFamille` (`Dictionary<int, decimal>`). `Historiques()` leur donne les valeurs
    exactement telles qu'elles étaient codées en dur dans
    `TaxDataService.InitializeDefaultData` (mêmes bornes contiguës 30000/50000/80000/...,
    mêmes taux 12,10 %/13,90 %/.../25 %, mêmes réductions 8 à 20 % pour 1 à 7 charges).
    `DepuisBareme(bareme)` résout les tranches en tout ou rien — contrairement à un taux
    scalaire, une table de tranches ne se complète pas champ par champ : si
    `bareme.Tranches` forme une table valide (au moins une tranche, la première à 0, bornes
    contiguës, la dernière ouverte, taux entre 0 et 100 %, mêmes contrôles que
    `BaremeLegal.Anomalies()` mais limités aux tranches), elle est prise telle quelle ;
    sinon repli sur la table historique. Les charges de famille, elles, se complètent charge
    par charge comme les autres champs de `DepuisBareme` (une charge non renseignée par le
    barème retombe sur la valeur historique). `TaxCalculatorService.CalculateGrossTax` lit
    maintenant `ParametresAppliques.Tranches` au lieu de `_taxDataService.TaxBrackets`, et
    les deux calculs de réduction pour charges de famille (`CalculateMonthlyTax`,
    `CalculateAnnualRegularization`) lisent `ParametresAppliques.ChargesFamille` au lieu de
    `_taxDataService.FamilyChargeRates`. `DiffereDesHistoriques`/`Ecarts()` (affichés sur
    l'écran « Contrôle d'équivalence ») comparent désormais aussi les tranches et les
    charges de famille. **Équivalence vérifiée** : le barème système livré avec chaque
    dossier (`BaremeService`, barème « en vigueur ») porte déjà exactement ces mêmes
    tranches et réductions depuis SOC-02 — un dossier qui n'a jamais dupliqué/modifié ce
    barème obtient donc un IUTS rigoureusement identique à avant ce branchement ; le contrôle
    d'équivalence (DEC-01), qui réinitialise `ParametresAppliques` à `Historiques()` avant de
    comparer, reste lui aussi inchangé. `RetenueSpeciale` (1 % en dur) reste volontairement
    non branchée : `BaremeLegal` n'a pas de champ pour ce taux, ajouter en brancher un est un
    changement de modèle distinct, non demandé ici.
    **Découvert au passage, non corrigé, à trancher séparément** : l'écran « Paramètres »
    (`SettingsViewModel`, menu Configuration) possède son propre éditeur de tranches d'IUTS
    et de taux pour charges de famille, enregistré dans `AppSettings` (table à part,
    `DataService.SaveSettingsAsync`) — mais rien dans le moteur de calcul ne lit jamais
    `AppSettings.TaxBrackets`/`FamilyCharges` : cet éditeur existait déjà avant ce
    branchement et reste, comme avant, sans aucun effet sur les bulletins. Un utilisateur
    qui modifierait le barème depuis cet écran croirait à tort avoir changé l'IUTS calculé.
    Deux devenirs possibles, à trancher avec le client : retirer cet écran (le barème daté,
    module SOC-02, est désormais le seul point d'entrée qui compte), ou le reconstruire pour
    qu'il édite le même barème daté plutôt qu'un réglage parallèle mort. Non traité ici pour
    ne pas mélanger une décision produit avec un branchement technique.

24. **Demandé le 16 septembre 2026 : « le reconstruire l'écran 'Paramètres' pour qu'il
    édite le même barème »** — suite du point 23 : le client a choisi de reconstruire plutôt
    que retirer l'écran « Paramètres ». `SettingsViewModel` n'édite plus `AppSettings.
    TaxBrackets`/`FamilyCharges` (retirés, ainsi que leurs valeurs par défaut câblées en dur
    et fausses depuis longtemps — code mort, jamais lu par le moteur de calcul, donc sans
    impact sur les bulletins déjà émis) : il édite désormais le barème daté « en vigueur »
    (`BaremeService.BaremeApplicableAsync(DateTime.Today)`), exactement le même objet que
    l'écran « Barèmes légaux ». Une fiche `FicheBareme` clonée (même garde « même barème »
    par Id qu'ailleurs, pour ne pas écraser une saisie en cours lors d'un rechargement en
    arrière-plan) porte les grilles Tranches/Charges familiales, avec un texte d'avertissement
    visible sur les deux onglets : modifier ici change rétroactivement le barème de TOUS les
    bulletins déjà calculés avec ce barème (contrairement à « Dupliquer » sur l'écran
    « Barèmes légaux », qui crée une nouvelle version datée sans toucher aux bulletins déjà
    émis). Les boutons Ajouter/Supprimer tranche et la renumérotation automatique des bornes
    ont été extraits de `BaremeViewModel` vers un nouvel utilitaire partagé
    `Helpers/EditionTranches.cs` (`Ajouter`, `Supprimer`, `Renumeroter`), repris tel quel par
    les deux écrans — pas de changement de comportement, juste la fin d'une duplication.
    L'enregistrement (bouton « Enregistrer ») valide d'abord la fiche barème
    (`BaremeLegal.Anomalies()`, mêmes règles que sur « Barèmes légaux »), refuse si une autre
    version du barème a déjà la même date d'effet, puis persiste via `BaremeService.
    EnregistrerAsync`. Le bouton « Réinitialiser » recharge les tranches/charges du barème
    légal initial (`BaremeService.BaremesInitiaux()`) dans la fiche de travail, sans
    enregistrer automatiquement — au clic sur « Réinitialiser » l'ancien code sauvegardait
    aussitôt ; c'est désormais à l'utilisateur de confirmer avec « Enregistrer ».
    **Corrigé au passage, bogue préexistant sans lien avec le barème** : `SaveSettingsAsync`
    construisait un `AppSettings` tout neuf avant de l'enregistrer, ce qui réinitialisait
    silencieusement à chaque clic sur « Enregistrer » les champs non touchés par cet écran
    (`JoursCongesParMois`, `BaseRetenueAbsence`, et surtout `AfficherDeviseSurBulletin`, un
    réglage utilisé par les écrans Bulletin/Bulletin en lot) à leurs valeurs par défaut C# —
    corrigé en relisant d'abord les réglages actuels (`DataService.LoadSettingsAsync`) avant
    de les modifier et de les réenregistrer, comme le fait déjà `UpdateSettingAsync` ailleurs
    dans le code.

25. **Erreur de build signalée le 16 septembre 2026 (capture d'écran, CS1729)** :
    `MainViewModel.cs` appelait `new AbsenceViewModel(absenceService, dataService, new
    AbsenceExcelService())` à 3 arguments, mais `AbsenceViewModel` n'avait qu'un constructeur
    à 2. En creusant, l'écart dépassait largement ce seul constructeur : `AbsenceView.xaml`
    (et les modèles `Absence`/`Employee`) étaient déjà prêts pour une recherche/filtres sur la
    liste des absences et pour une « attribution groupée » de congés (tout l'effectif ou par
    catégorie), mais rien côté `AbsenceViewModel` ne les servait — build cassé par un montage
    resté à mi-chemin. Sur confirmation du client (« tout construire »), complété en une seule
    fois :
    — *Import/export Excel* (`AbsenceExcelService`, déjà écrit, jamais branché) :
    `ExporterModeleCommand` génère un classeur pré-rempli avec l'effectif et son solde de
    congés (bouton « Modèle Excel ») ; `ImporterExcelCommand` (bouton « Importer Excel »)
    relit un fichier complété et enregistre en masse via `AbsenceService.
    EnregistrerEnMasseAsync` — qui valide tout le lot avant d'écrire quoi que ce soit, donc un
    salarié en anomalie (chevauchement, période close...) annule l'import entier plutôt que
    d'en enregistrer une partie ; le détail des lignes ignorées s'affiche dans une boîte de
    dialogue.
    — *Recherche et filtres* sur la liste des absences : `SearchText` (nom, type, motif,
    matricule), `SelectedTypeFilter` et `SelectedRemunereeFilter`, exposés via une
    `ICollectionView` (`AbsencesView`), même schéma que les filtres déjà en place sur
    « Gestion des employés » (`EmployeeManagementViewModel`). Une recherche similaire
    (`FiltreSalarie`) filtre aussi le sélecteur de salarié du formulaire d'édition.
    — *Attribution groupée de congés* (panneau rétractable, `ToggleAttributionGroupeeCommand`) :
    choix d'une catégorie ou de tout l'effectif (`CategoriesFiltre`/`CategorieFiltreSelectionnee`),
    dates et jours ouvrables calculés (`CalculerJoursGroupeeCommand`, même règle que le calcul
    unitaire), appliqués en un seul lot via `AppliquerAttributionGroupeeCommand` →
    `AbsenceService.EnregistrerEnMasseAsync`. Seuls les salariés actifs (`Employee.EstActif`)
    sont ciblés, comme pour les écrans de lot existants (dette n°20) — un salarié sorti ou
    suspendu ne reçoit pas de congé collectif. Là aussi, tout le lot est refusé si un seul
    salarié pose problème.
    Aucun changement dans `AbsenceView.xaml`, déjà correcte : uniquement `AbsenceViewModel.cs`
    complété pour la servir.

26. **Livré le 17 septembre 2026 : RH-01 — Organigramme d'entreprise** :
    Premier module du volet Ressources Humaines (Phase 4).
    — *Modèle relationnel et hiérarchique* (`Models/Organigramme.cs`) : `TypeUniteOrganisationnelle`
    (DirectionGenerale, Direction, Departement, Service, Bureau, Equipe), `UniteOrganisationnelle`
    avec code, libellé, rattachement parent `ParentId`, responsable actuel ; `Poste` avec code,
    intitulé, rattachement structurel `UniteId`, rattachement hiérarchique N+1 `SuperviseurPosteId`,
    effectif cible et effectif actuel ; `NoeudOrganigramme` pour la projection arborescente WPF
    avec calcul récursif des effectifs (directs et consolidés sous-arbre), code couleur/badge par
    type d'unité, avatars graphiques de direction.
    — *Détection stricte de cycles hiérarchiques* (`OrganigrammeService.cs`) : méthodes
    `DetecterCycleUnite(uniteId, parentCandidatId)` et `DetecterCyclePoste(posteId, superviseurCandidatId)`
    empêchant toute boucle hiérarchique (un parent ne pouvant devenir son propre descendant,
    auto-référence interdite).
    — *Base de données et persistance* (`SQLiteService.cs` migration 29 + `DataService.cs`) :
    création des tables `UnitesOrganisationnelles` et `Postes`, ajout des colonnes `PosteId` et
    `ManagerMatricule` dans `Employees`. `CreerSchemaComplet` intègre directement ces structures
    pour les nouveaux dossiers. Initialisation automatique d'une structure organisationnelle
    type burkinabè (DG, DAF, DRH, Direction Commerciale, Services Comptabilité & Paie).
    — *Double export* (`OrganigrammeExportService.cs`) :
    (1) Export Excel ClosedXML à 3 feuilles : « Structure Organisationnelle » (tableau hiérarchique
    des directions/services), « Référentiel des Postes » (liste des postes, unités, N+1 et effectifs),
    « Salariés & Affectations » (liste nominative avec poste, service et supérieur hiérarchique).
    (2) Export PDF iTextSharp A4 officiel : cartouche employeur avec IFU et raison sociale du dossier
    courant, synthèse des unités avec effectifs directs et consolidés, hiérarchie N+1 des postes et
    blocs de signature de validation RH.
    — *Interface graphique WPF moderne* (`OrganigrammeView.xaml`, `OrganigrammeViewModel.cs`) :
    4 onglets de navigation : (0) Arbre hiérarchique interactif avec zoom (50% à 200%), recherche et
    mise en surbrillance, badges colorés et avatars ; (1) Gestion des Directions & Services (CRUD,
    sélection du parent avec filtrage anti-cycle) ; (2) Référentiel des Postes & Fonctions (CRUD,
    sélection du service et du superviseur N+1) ; (3) Affectation rapide des salariés (modification
    directe du service, du poste et du manager).
    — *Navigation globale et anti-staleness* : nouvelle section « RESSOURCES HUMAINES » dans la barre
    latérale de `MainWindow.xaml`, intégration dans le moteur de recherche du menu avec mots-clés RH,
    et respect du cycle de vie `OnIsActiveChanged` pour le rafraîchissement temps réel des effectifs.

27. **Livré le 17 septembre 2026 : RH-02 — Postes & fiches de poste** :
    Deuxième module du volet Ressources Humaines (Phase 4).
    — *Modèle relationnel et fiches de poste* (`Models/FichePoste.cs`, `Models/Organigramme.cs`) :
    extension de `Poste` avec finalité principale, activités clés & tâches régulières, niveau d'étude requis,
    diplôme exigé, expérience minimale requise, conditions particulières de travail, convention collective de rattachement,
    catégorie et échelon conventionnels indicatifs, salaire minimum conventionnel issu de la grille SOC-05,
    fourchette de salaire budgétée (min - max).
    — *Catalogue des compétences d'entreprise* : `DomaineCompetence` (Technique, Management, Relationnel, Organisationnel, JuridiqueEtReglementaire),
    `NiveauMaitrise` (Notions 1 ★, Pratique 2 ★★, Confirme 3 ★★★, Expert 4 ★★★★), `Competence` d'entreprise
    avec description et critères d'évaluation, `PosteCompetence` associant une compétence requise à un poste avec
    niveau d'exigence et caractère obligatoire. Migration 30 pré-alimente 10 compétences de référence adaptées
    au tissu économique burkinabè (SYSCOHADA, paie, droit du travail, gestion de projets, management, etc.).
    — *Cardinalité GPEC et décompte dynamique des effectifs (1 Poste ↔ N Salariés)* :
    l'effectif pourvu réel est calculé dynamiquement par `COUNT(*) FROM Employees WHERE PosteId = @id AND StatutSalarie = 0` (salariés actifs uniquement).
    Quatre statuts d'occupation GPEC stricts : `Vacant` (0 pourvu sur N requis), `Partiel` (0 < pourvu < requis),
    `Pourvu` (pourvu == requis), `Sureffectif` (pourvu > requis). Badges graphiques colorés (rouge, ambre, vert, violet).
    — *Nouvel écran transverse « Suivi budgétaire des postes »* (`Views/SuiviPostesBudgetView.xaml`, `ViewModels/SuiviPostesBudgetViewModel.cs`) :
    deux volets : (0) Tableau de bord « Budget vs Pourvu » avec 6 cartes d'indicateurs KPIs (Postes ouverts, Effectif budgété,
    Effectif pourvu, Postes vacants, Sureffectif, Taux d'occupation global), filtres combinés par statut et direction/service,
    recherche instantanée, DataGrid détaillé et volet inférieur maître-détail affichant les titulaires réels et les compétences
    requises du poste sélectionné ; (1) Référentiel des Compétences d'entreprise avec catalogue et formulaire d'édition CRUD.
    — *Éditeur enrichi dans l'Organigramme* (`Views/OrganigrammeView.xaml`, `ViewModels/OrganigrammeViewModel.cs`) :
    l'onglet « Postes & Fonctions » intègre la fiche de poste complète découpée en 6 sections professionnelles,
    avec ajout rapide de compétences depuis le catalogue, affichage des titulaires réels multiples, et bouton d'export direct.
    — *Double export* (`Services/FichePosteExportService.cs`) :
    (1) Fiche de poste PDF A4 officielle iTextSharp : cartouche employeur IFU/RCCM, 7 sections structurées
    (identification, finalité, activités clés, profil requis, classification conventionnelle, compétences requises
    avec étoiles de niveau, titulaires actuels) et pavé de signatures tripartites (Le Titulaire, Le Supérieur N+1, La Direction RH).
    (2) Synthèse Excel ClosedXML à 3 feuilles : « Budget vs Pourvu » (tableau GPEC avec alertes d'occupation),
    « Nomenclature des Postes » (spécifications complètes des fiches de poste) et « Catalogue Compétences » (répertoire société).
    — *Tests unitaires* (`IUTSCalculator_Tests/FichePosteTests.cs`) : validation complète du décompte GPEC sur les 4 statuts
    (en ignorant les salariés sortis/inactifs), CRUD catalogue et affectation des compétences, consultation du salaire minimum conventionnel,
    et génération conforme des fichiers PDF et Excel sur disque.
    — **Correctifs du 18 septembre 2026, suite à un audit de conformité au plan approuvé** : l'implémentation initiale (livrée
    en dehors de cette conversation) présentait deux écarts par rapport aux décisions actées avec l'utilisatrice avant le
    développement. (1) *Bug confirmé* : `Poste.EstPourvu` combinait le nouveau décompte GPEC (`EffectifPourvu > 0`) avec un
    reliquat de l'ancien champ mono-titulaire (`!string.IsNullOrWhiteSpace(TitulaireMatricule)`), alors que ce champ n'est
    plus mis à jour lorsqu'un salarié quitte un poste via l'onglet « Affectation rapide » (`AffecterSalarieAsync` ne touche
    que `Employees.PosteId`, jamais `Postes.TitulaireMatricule`). Conséquence réelle : les cartes KPI « Postes pourvus /
    vacants » et le filtre du même nom (onglet Organigramme) pouvaient déclarer un poste « pourvu » alors que l'arbre et le
    badge GPEC le montraient à juste titre « Vacant ». Corrigé en simplifiant `EstPourvu` en pur alias du décompte GPEC
    (`_effectifPourvu > 0`). (2) *Écart de nommage* : le champ avait été implémenté sous le nom `ConventionNom` alors que le
    renommage en `ConventionLibelle` (cohérence avec `UniteLibelle` et `ConventionCollective.Libelle`) avait été explicitement
    validé avant le développement. Renommé dans `Models/Organigramme.cs`, `Services/OrganigrammeService.cs` (alias SQL et
    lecture), `ViewModels/OrganigrammeViewModel.cs`, `Views/OrganigrammeView.xaml` et `IUTSCalculator_Tests/FichePosteTests.cs`.
    Le reste de l'implémentation RH-02 (schéma, décompte GPEC, service de fiches de poste, exports PDF/Excel, écran transverse,
    non-duplication de l'éditeur de poste, non-écrasement des champs `Employee.Category/ConventionId/Echelon`) a été vérifié
    conforme au plan et à l'architecture GPEC décidée. Point à surveiller, non corrigé (choix d'affichage, pas un bug) : pour
    un poste GPEC à plusieurs titulaires, l'arbre organisationnel n'affiche que le nom/avatar du dernier titulaire renseigné
    via l'éditeur de poste (`TitulaireNom`/`TitulaireMatricule`), pas la liste complète — seul le badge d'effectif (ex.
    « 2/2 ») reflète le nombre réel.
    — **Correctif de disposition, même date** : signalé par la cliente sur capture d'écran — l'onglet « Tableau de bord Budget
    vs Pourvu » de `SuiviPostesBudgetView.xaml` répartissait tout son contenu (cartes KPI, filtres, tableau des postes, volet
    détail Titulaires/Compétences) en lignes de grille figées (`3*`/`2*`, sans `MinHeight`), sans `ScrollViewer` global — contrairement
    au reste du module RH (`OrganigrammeView.xaml` protège sa zone principale avec `MinHeight="350"` et enveloppe ses formulaires
    longs dans un `ScrollViewer`). À la hauteur de fenêtre de conception (780px), le tableau des postes n'affichait que 2 lignes
    avec ascenseur et le volet détail était réduit à ses seuls en-têtes, sans aucune ligne visible. Corrigé en enveloppant tout
    l'onglet dans un `ScrollViewer` vertical et en donnant un plancher (`MinHeight="200"` / `"220"`) aux deux lignes concernées :
    la répartition proportionnelle reste inchangée quand la fenêtre est assez haute, et un ascenseur de page apparaît sinon —
    au lieu d'un volet écrasé à l'invisibilité. Fichier modifié : `Views/SuiviPostesBudgetView.xaml`. À vérifier visuellement
    dans Visual Studio (changement de mise en page WPF, non testable sans compilateur ni rendu depuis cette conversation).

28. **Livré le 18 septembre 2026 : RH-03 — Entretiens & Évaluations** :
    Troisième module du volet Ressources Humaines (Phase 4).
    — *Modèle de données relationnel étendu (migration 31)* (`Models/Evaluation.cs`, `Services/SQLiteService.cs`) :
    création de 5 tables spécialisées : (1) `CampagnesEvaluation` (Code unique, Titre, AnneeExercice, DateDebut, DateFin,
    Statut 0-3, Description, DateCreation) ; (2) `EntretiensEvaluation` (CampagneId, EmployeeId, EvaluateurMatricule nullable avec
    `ON DELETE SET NULL`, DateEntretien, Statut 0-4, NoteGlobale TEXT invariant, PointsForts, AxesProgres, CommentairesSalarie,
    CommentairesEvaluateur, AvisDirectionRH, SouhaitsEvolution, DateValidation, DateCreation) ; (3) `EntretienCriteres`
    (EntretienId, CompetenceId nullable vers `Competences(Id) ON DELETE SET NULL`, Categorie, Libelle, Note 1-5, Poids/Coefficient,
    Commentaire) ; (4) `EntretienObjectifs` (EntretienId, TypeObjectif 0-1 pour Passé N-1 vs Nouveau N+1, Intitule, IndicateurMesure,
    CibleAttendue, ResultatObtenu, TauxAtteintePct, Appreciation, Echeance, Poids) ; (5) `EntretienBesoinsFormation`
    (EntretienId, CompetenceId nullable vers `Competences(Id)`, ThemeFormation, Priorite 1-3, Statut 0-3 pour passerelle RH-04).
    Index unique strict `UX_EntretiensEvaluation_Campagne_Employe` sur `(CampagneId, EmployeeId)`.
    Conformité d'intégrité : **aucun `ON DELETE CASCADE` sur `EmployeeId`** pour préserver l'historique d'évaluation opposable
    des collaborateurs en cas de départ.
    — *Règles d'immutabilité et recalcul dynamique de la notation* :
    `NoteGlobale` (moyenne pondérée sur 5 des critères) recalculée immédiatement à chaque mise à jour de critère tant que
    l'entretien est en phase préparatoire ou de conduite (`Statut < Valide`). Dès que l'entretien est visé/signé (`Statut >= Valide`),
    le document est scellé avec sa date de validation : toute tentative de modifier l'entretien, ses critères, ses objectifs
    ou ses besoins de formation est rejetée au niveau du service métier (`EvaluationService`) par une exception `InvalidOperationException`
    (garantie juridique et légale des évaluations signées).
    — *Initialisation de campagne idempotente et ciblage actif* :
    ciblage exclusif de l'effectif actif conforme à `Employee.EstActif` (`StatutSalarie == 0` et absence de `DateSortie` dans le passé),
    éliminant les salariés ayant quitté l'entreprise. Traitement idempotent : vérification d'existence préalable évitant
    toute violation d'index ou doublon en cas de réouverture de campagne, avec rapport détaillé (salariés actifs, créés, existants).
    — *Algorithme de résolution d'évaluateur N+1 à 3 étapes* :
    (1) `Employee.ManagerMatricule` explicite prioritaire ; (2) Dérivation depuis l'organigramme RH-01 via
    `Employee.PosteId` -> `Poste.SuperieurPosteId` -> titulaires actifs trouvés via `OrganigrammeService.ChargerTitulairesPosteAsync` ;
    (3) En cas d'ambiguïté (vacance, absence de supérieur, pluralité de titulaires), aucun évaluateur n'est deviné :
    le champ est laissé vide et le collaborateur remonte dans la liste des « Évaluateurs à assigner manuellement » avec badge ambre.
    — *Passerelle avec le référentiel de compétences RH-02* :
    quand un critère est rattaché à une compétence catalographiée, sa `Categorie` hérite automatiquement du domaine de la compétence
    (`DomaineLibelle` : Technique, Managériale, Comportementale, Réglementaire). Les besoins de formation enregistrent également
    le `CompetenceId` optionnel pour consolidation ultérieure par le module RH-04.
    — *Interface utilisateur MVVM complète (3 onglets)* (`Views/EntretiensEvaluationsView.xaml`, `ViewModels/EntretiensEvaluationsViewModel.cs`) :
    enveloppée dans un `ScrollViewer` global avec `MinHeight` pour proscrire tout écrasement vertical sur petit écran :
    (0) « Campagnes d'Évaluation » : bandeau KPIs (salariés cibles, planifiés, en cours, validés, non assignés, note moyenne de campagne),
    tableau de bord des campagnes, formulaires de création/clôture et initialisation automatique en lot ;
    (1) « Conduite de l'Entretien » : sélecteur de campagne et de salarié, cartes d'identité collaborateur et évaluateur,
    panneau de contrôle du statut (Planifié -> En cours -> Réalisé -> Validé & Signé -> Clôturé), volet d'évaluation multicritères
    avec étoiles et moyenne en temps réel, volet des objectifs N-1 (taux d'atteinte moyen pondéré) et N+1, recueil des besoins
    de formation, bilans qualitatifs (points forts, axes de progrès, avis RH), et verrouillage automatique des contrôles en lecture seule
    avec badge cadenas émeraude dès validation ;
    (2) « Historique Collaborateur » : trajectoire pluriannuelle des évaluations avec courbe de notation, historique des objectifs
    et mention globale par exercice.
    — *Double export réglementaire et de pilotage* (`Services/EvaluationExportService.cs`) :
    (1) Fiche officielle d'évaluation individuelle en PDF A4 (iTextSharp) : cartouche employeur IFU/RCCM, 6 sections structurées
    (collaborateur & poste, évaluateur, grille des compétences notées, objectifs N-1 & N+1, besoins de formation, synthèse & appréciations)
    et pavé officiel de triple signature (Le Collaborateur, L'Évaluateur N+1, La Direction RH).
    (2) Classeur Excel de synthèse de campagne à 4 feuilles (ClosedXML) : « Synthèse Campagne » (baromètre, KPIs, répartition des mentions),
    « Relevé des Évaluations » (tableau nominatif complet des notes et statuts), « Objectifs Fixés N+1 » (portefeuille d'objectifs consolidé),
    et « Besoins de Formation » (état consolidé des demandes préparant RH-04).
    — *Câblage MainWindow et navigation* :
    DataTemplate dédié, bouton de menu sous `RESSOURCES HUMAINES` avec propriété de visibilité `IsNavEntretiensVisible`,
    enregistrement dans `_navMenuItems` avec mots-clés RH pour la recherche globale, et liaison MVVM dans `MainViewModel.cs`.
    — *Validation et tests unitaires* (`IUTSCalculator_Tests/EvaluationTests.cs`) :
    7 tests unitaires automatisés validant la migration 31, le CRUD de campagne, le filtre `EstActif`, l'idempotence,
    la hiérarchie d'évaluateur N+1 à 3 étapes, le calcul de la note globale et le verrouillage d'immutabilité, le cycle d'objectifs/formations,
    et la génération effective des exports PDF et Excel sur disque.

- **18 septembre 2026 — livraison du module RH-04 « Formation » (Plan annuel, catalogue, sessions, inscriptions, pointage présence, attestations officielles A4, verrou probatoire, passerelle RH-03, tableau de bord TPA, exports Excel 4 feuilles).**
    — *Schéma de données (Migration 32, `SchemaCible = 32`)* :
    création de 5 tables relationnelles : (1) `PlansFormation` (Id, Code, Titre, AnneeExercice, BudgetPrevisionnel,
    Statut 0-4 : Élaboration, Soumis, Validé, En cours, Clôturé, DateValidation, DateCreation, ObjectifsStrategiques) ;
    (2) `OrganismesFormation` (Id, RaisonSociale, ContactNom, Telephone, Email, Adresse, NumeroAgrement [ex: FAFPA], Specialites, Actif) ;
    (3) `FormationsCatalogue` (Id, Code, Intitule, Domaine, CompetenceViseeId nullable vers `Competences(Id)`, TypeFormation 0-2 : Interne, Externe, En ligne,
    DureeHeures, CoutEstimeParParticipant, ObjectifsPedagogiques, Prerequis, Actif) ;
    (4) `SessionsFormation` (Id, PlanFormationId, FormationId, OrganismeId nullable, CodeSession, IntituleSession, FormateurNom,
    DateDebut, DateFin, Lieu, NombrePlaces, CoutPedagogique, CoutLogistique, CoutAutre, Statut 0-4 : Planifiée, Confirmée, En cours, Réalisée, Annulée,
    DateCreation, Commentaire) avec `CoutTotalReel` calculé comme la somme des trois composantes de coûts ;
    (5) `SessionInscriptions` (Id, SessionId, EmployeeId, DateInscription, StatutPresence 0-4 : Inscrit, Présent, Absent justifié, Absent injustifié, Abandon,
    NoteEvaluation, AttestationObtenue 0/1, FraisImputes, Commentaire).
    Index unique strict anti-doublon `UX_SessionInscriptions_Session_Employe` sur `(SessionId, EmployeeId)`.
    Extension de la table RH-03 `EntretienBesoinsFormation` avec la colonne `SessionFormationId INTEGER` indexée pour la passerelle de consolidation.
    Amorçage de 2 organismes de référence (DRH Interne et IBMI agréé FAFPA) et de 3 actions de formation au catalogue reliées aux compétences.
    — *Architecture probatoire et cascades de suppression C# explicites en transactions atomiques* :
    en raison de l'absence de `PRAGMA foreign_keys` actif sous SQLite, les cascades ont été rigoureusement implémentées en C# (`FormationService`) :
    suppression d'une session -> nettoyage des inscriptions et détachement gracieux (`SessionFormationId = NULL`) des besoins RH-03 liés sans suppression des besoins ;
    suppression d'un plan -> suppression en cascade de toutes ses sessions et inscriptions.
    **Verrou probatoire inviolable** : toute tentative de suppression d'une session ou d'un plan rattaché à au moins une inscription dont l'attestation a été délivrée (`AttestationObtenue = 1`)
    est catégoriquement rejetée par une `InvalidOperationException` afin de garantir l'opposabilité juridique des attestations délivrées face au fisc et aux inspecteurs du travail.
    — *Passerelle bidirectionnelle avec les entretiens RH-03* :
    `AssignerBesoinASessionAsync` lie le besoin à la session et fait basculer son statut à `Retenu` (1). `DetacherBesoinSessionAsync` réinitialise la liaison sans altérer l'historique du besoin.
    — *Tableau de bord TPA (Taxe Patronale et d'Apprentissage)* :
    arbitrage fiscal calculant la masse salariale soumise TPA cumulée sur les bulletins normaux (en excluant les régularisations `IsRegularization = 1` et récapitulatifs annuels `IsAnnual = 1`),
    la TPA théorique due au taux légal, le total des dépenses réelles de formation engagées, le taux de couverture TPA (%), et le solde restant dû ou crédit de formation.
    — *Interface utilisateur MVVM complète (4 onglets)* (`Views/FormationView.xaml`, `ViewModels/FormationViewModel.cs`) :
    conforme aux conventions d'ergonomie du projet avec `ScrollViewer` global, `MinHeight=750`, pinceaux et icônes vectorielles réelles :
    (0) « Plans de Formation » : indicateurs clés (budget prévisionnel, coûts réels engagés, écart budgétaire, sessions, stagiaires formés), liste des plans, formulaires et validation ;
    (1) « Sessions & Inscriptions » : filtrage par plan et statut, gestion complète des sessions, détail financier 3 composantes, liste des participants avec badge de statut, bouton de pointage de présence et délivrance d'attestation ;
    (2) « Catalogue & Organismes » : double gestionnaire catalogue de formation (avec compétence visée) et organismes agréés FAFPA / internes ;
    (3) « Arbitrage TPA & Besoins » : tableau de bord de couverture TPA avec jauges et indicateurs financiers, et recueil consolidé des besoins issus des évaluations RH-03 avec assignation directe à une session.
    — *Double export réglementaire et de pilotage* (`Services/FormationExportService.cs`) :
    (1) Attestation officielle de formation A4 individuelle (iTextSharp) : cartouche d'entreprise IFU/RCCM, identification complète du bénéficiaire, intitulé de l'action, dates, volume horaire, mention de réussite, et pavé de double visa (L'Organisme / Formateur et La Direction Générale / DRH).
    (2) Classeur Excel de bilan annuel de formation à 4 feuilles (ClosedXML) : « Synthèse Bilan & TPA » (KPIs, baromètre TPA), « Sessions de Formation » (registre détaillé des sessions et coûts), « Registre des Stagiaires » (tableau nominatif avec présences et attestations), et « Besoins Recueillis » (état consolidé des demandes d'évaluation).
    — *Câblage MainWindow et navigation* :
    DataTemplate dédié, bouton de menu sous `RESSOURCES HUMAINES` avec propriété de visibilité `IsNavFormationVisible`,
    enregistrement dans `_navMenuItems` avec mots-clés RH pour la recherche globale, et liaison MVVM dans `MainViewModel.cs`.
    — *Validation et tests unitaires* (`IUTSCalculator_Tests/FormationTests.cs`) :
    10 tests unitaires automatisés validant la migration 32, le CRUD des plans, le catalogue et organismes avec compétence liée, le calcul du coût total réel,
    l'anti-doublon d'inscription et le pointage de présence, le verrou probatoire strict interdisant la suppression, la cascade C# en transaction atomique,
    la passerelle RH-03 avec assignation et détachement gracieux, l'arbitrage fiscal TPA avec exclusion des régularisations, et la génération effective de l'attestation PDF sur disque.
    - **18 septembre 2026 — livraison du module RH-05 « Recrutement & Candidatures » (Expression de besoin, vivier candidats, sélection multi-étapes, entretiens et notation, intégration directe et atomique en fiche salarié SAL-01, affectation de poste RH-01, garde-fou anti-sur-recrutement, verrou probatoire inviolable, lettre d'embauche officielle A4 et bilan ClosedXML 4 feuilles).**
    — *Schéma de données (Migration 33, `SchemaCible = 33`)* :
    création de 4 tables relationnelles avec double point d'ancrage (`EnsureSchema` et `CreerSchemaComplet`) :
    (1) `RecrutementBesoins` (Id, Reference [ex: REC-2026-001], IntitulePoste, Departement, PosteId nullable vers `Postes(Id)`,
    Motif 0-4 : Creation, Remplacement, SurcroitActivite, StagePreEmbauche, Autre, NombrePostes [>=1], NombreRecrutes [>=0],
    SalaireProposeMin, SalaireProposeMax, TypeContratPrevu [CDI, CDD, Stage...], ProfilRecherche, DateExpression, DateEcheanceSouhaitee,
    Statut 0-4 : Brouillon, Ouvert, EnCours, Pourvu, Annule, DateCloture, DateCreation, Commentaire) ;
    (2) `RecrutementCandidats` (Id, Nom, Prenoms, Email, Telephone, Adresse, Ville, Nationalite, DiplomeLePlusEleve, AnneesExperience,
    DernierPosteOccupe, CompetencesCles, PretentionSalariale, Disponibilite, CvLien, Actif 0/1 [vivier], DateCreation, NotesInternes) ;
    (3) `RecrutementCandidatures` (Id, BesoinRecrutementId vers `RecrutementBesoins(Id)`, CandidatId vers `RecrutementCandidats(Id)`,
    DatePostulation, CanalSource 0-5 : SiteWeb, Cooptation, CabinetRecrutement, ReseauxSociaux, CandidatureSpontanee, Autre,
    PretentionSalariale, Statut 0-5 : Recue, EnEvaluation, Retenue, Rejetee, Embauchee, Vivier,
    EtapeActuelle 0-7 : Reception, TriCv, EntretienTelephonique, EntretienRh, TestTechnique, EntretienManager, NegociationSalariale, Finalisee,
    ScoreGlobal, CommentaireRecruteur, DateEmbauche, SalaireNegocie, SalarieCreeMatricule vers `Employees(EmployeeId)`, DateCloture) ;
    (4) `RecrutementEntretiens` (Id, CandidatureId vers `RecrutementCandidatures(Id)`, Etape 0-7, DateEntretien, Heure, EvaluateurMatricule,
    EvaluateurNom, Modalite 0-2 : Presentiel, Visioconference, Telephonique, Lieu, Avis 0-3 : Favorable, Reserve, Defavorable, NonEvalue,
    NoteSur5 [0.0 - 5.0], PointsForts, AxesAmelioration, Synthese, DateCreation).
    Index unique strict anti-doublon `UX_RecrutementCandidatures_Besoin_Candidat` sur `(BesoinRecrutementId, CandidatId)` interdisant qu'un candidat postule deux fois au même besoin.
    Index d'accélération sur `RecrutementBesoins.PosteId`, `RecrutementCandidatures.BesoinRecrutementId`, `RecrutementCandidatures.CandidatId` et `RecrutementEntretiens.CandidatureId`.
    Amorçage initial d'un besoin de référence (Responsable Paie & Administration) et de deux profils dans le vivier.
    — *Passerelle directe d'embauche SAL-01 & organigramme RH-01 atomique* :
    `IntegrerCandidatEnSalarieAsync` réalise au sein d'une **seule transaction SQLite atomique** :
    (1) Génération du matricule automatique `EMP-XXXX` via `DataService.GenerateNextEmployeeIdAsync` ;
    (2) Insertion complète dans `Employees` via la surcharge interne `_dataService.AddEmployeeAsync(nouveauSalarie, conn, trans)` (civilité, nom/prénoms, date de naissance déduite ou par défaut, fonction héritée de l'intitulé du besoin, salaire de base négocié, date d'entrée, service/département, type de contrat, statut actif, et affectation optionnelle du `PosteId`) ;
    (3) Si le besoin était rattaché à un poste (`PosteId`), mise à jour directe dans `Postes` du champ `TitulaireMatricule` (`UPDATE Postes SET TitulaireMatricule = @Matricule WHERE Id = @PosteId`) pour matérialiser instantanément l'affectation dans l'organigramme RH-01 et les effectifs pourvus RH-02 ;
    (4) Marquage de la candidature en `Statut = Embauchee` (4), `EtapeActuelle = Finalisee` (7), horodatage de la date d'embauche, salaire négocié et liaison matricule `SalarieCreeMatricule` ;
    (5) Incrémentation de `NombreRecrutes` sur le besoin, et si `NombreRecrutes >= NombrePostes`, basculement automatique du besoin en `Statut = Pourvu` (3) avec horodatage de `DateCloture`.
    — *Garde-fou anti-sur-recrutement & intégrité probatoire étendue* :
    (1) Garde-fou strict interdisant l'embauche si le quota de postes à pourvoir est déjà atteint (`nbRecrutes >= nbPostes` ou `Statut == Pourvu`), empêchant toute embauche excédentaire non budgétée sans révision préalable du quota par la direction ;
    (2) Verrou probatoire étendu : interdiction absolue (`InvalidOperationException`) de supprimer un besoin ayant donné lieu à au moins une embauche (`NombreRecrutes > 0` ou candidatures embauchées liées) ;
    (3) Interdiction de supprimer un candidat du vivier dont une candidature a abouti à une embauche (garantie de traçabilité légale du dossier d'embauche) ;
    (4) Interdiction de modifier ou supprimer une candidature déjà validée en statut `Embauchee`.
    — *Calcul dynamique du score global de candidature* :
    à chaque création, modification ou suppression d'un entretien dans `RecrutementEntretiens`, la note moyenne pondérée est recalculée et mise à jour automatiquement sur la candidature parente dans `RecrutementCandidatures.ScoreGlobal`.
    — *Interface utilisateur MVVM complète (4 onglets)* (`Views/RecrutementView.xaml`, `ViewModels/RecrutementViewModel.cs`) :
    conforme aux standards d'excellence du projet avec `ScrollViewer` global, `MinHeight=750`, palettes contrastées, badges d'état et icônes vectorielles réelles :
    (0) « Besoins de Recrutement » : bandeau de 5 KPIs dynamiques (Postes ouverts, Postes pourvus, Candidatures totales, Entretiens planifiés, Embauches réalisées), tableau de bord des besoins avec barre de progression des recrutements, formulaires d'expression de besoin avec sélecteur de postes RH-01 et motifs légaux ;
    (1) « Vivier de Candidats » : gestion complète des profils candidats, filtrage par disponibilité, diplôme et texte libre, fiches de compétences et historique de candidatures ;
    (2) « Candidatures & Sélections » : suivi du pipeline par besoin et par étape (Réception -> Tri -> Entretien RH -> Test technique -> Négociation -> Finalisée), panneau de conduite d'entretien avec notation sur 5 et avis recruteur, historique des entretiens ;
    (3) « Embauche & Intégration SAL-01 » : formulaire d'intégration directe pré-rempli (salaire négocié, date de prise de fonction, type de contrat, catégorie, service), bouton d'action directe vers la fiche salarié, et panneau d'édition de la lettre d'embauche officielle.
    — *Double export réglementaire et de pilotage* (`Services/RecrutementExportService.cs`) :
    (1) Lettre d'embauche officielle A4 individuelle (iTextSharp) : cartouche d'entreprise complet (Raison sociale, IFU, RCCM, Adresse, Téléphone), référence de recrutement, identification du candidat retenu, rappel formel des conditions d'embauche (fonction, département, salaire mensuel brut en FCFA, date de prise de fonction, type de contrat), mention expresse de la période d'essai et de son renouvellement éventuel conformément aux **articles 38 à 43 de la loi n° 028-2008/AN portant Code du travail du Burkina Faso**, et pavé de double signature officielle (L'Employeur / Directeur Général et Le Salarié avec mention manuscrite « Lu et approuvé ») ;
    (2) Classeur Excel de synthèse du recrutement à 4 feuilles (ClosedXML) : « Synthèse Recrutement » (baromètre, KPIs, répartition des motifs et des canaux de sourcing), « Registre des Besoins » (tableau détaillé des postes à pourvoir et statuts), « Vivier des Candidats » (profils, diplômes, expériences, compétences clés et prétentions), et « Suivi des Candidatures » (pipeline nominatif complet avec scores, étapes, dates d'embauche et matricules générés).
    — *Câblage MainWindow et navigation* :
    DataTemplate dédié, bouton de menu sous `RESSOURCES HUMAINES` avec propriété de visibilité `IsNavRecrutementVisible`,
    enregistrement dans `_navMenuItems` avec mots-clés RH pour la recherche globale, et liaison MVVM dans `MainViewModel.cs`.
    — *Validation et tests unitaires* (`IUTSCalculator_Tests/RecrutementTests.cs`) :
    11 tests unitaires automatisés validant la migration 33, le CRUD et les transitions de statut des besoins, le vivier de candidats avec réactivation, le cycle des candidatures et avancement d'étapes, la notation d'entretiens et recalcul automatique du score global, la passerelle atomique d'embauche SAL-01 avec création salarié et affectation poste, le garde-fou anti-sur-recrutement, le triple verrou probatoire (besoin, candidat, candidature embauchée), et la génération sur disque des exports PDF et Excel.
    100% des 87 tests de la solution passent avec succès.
    - **19 octobre 2026 — livraison du module RH-08 « Gestion des temps & rotations », par anticipation sur RH-06/RH-07 (demande expresse du client), et passage de PAI-03 à cinq tranches sur barème daté.**
    — *Origine* : l'onglet « 01 » de l'état de salaire KS (Koswende Services, site de Youga) — planning de rotation 7 repos /
    7 jours / 7 nuits en postes de 12 h, décompte hebdomadaire des HS (15 % 41e-48e h, 35 % au-delà, 50 % nuit, 60 % et 120 %
    dimanche/férié de jour et de nuit), primes de panier. L'analyse du classeur a relevé deux défauts de formules, corrigés dans
    le moteur : (1) les tranches nuit / férié pouvaient compter deux fois les mêmes heures quand une semaine mêlait nuits et
    fériés (3 N + 1 FN : 12 h payées pour 8 h réelles) ; (2) le poste férié de JOUR se mesurait sur la durée du poste de nuit.
    — *Choix du client (questionnaire du 19/10/2026)* : coder RH-08 maintenant ; taux HS sur barème daté, défaut 15/35/50/60/120 ;
    semaine de décompte paramétrable par équipe (civile ou de rotation) ; période de pointage à jour d'arrêté paramétrable.
    — *Barème daté des HS* (`Models/BaremeHeuresSup.cs`, `Services/BaremeHeuresSupService.cs`, table `BaremesHeuresSup`) :
    deux barèmes livrés, `EstSysteme` : « Taux initiaux du module PAI-03 » au 01/01/2000 (15/30/50/**50**/100 — le dimanche de
    jour était majoré comme la nuit) et « cinq tranches » au **01/10/2026** (15/35/50/60/120). Le barème d'un mois est celui de
    date d'effet la plus récente ≤ 1er jour du mois ; deux barèmes ne partagent jamais une date. Édition dans l'onglet
    « Paramètres, fériés & barème HS » de l'écran RH-08 (Nouveau = copie datée du mois affiché). Le nouveau Code du travail
    (loi n°013-2026/ALP du 6 mai 2026) renvoie les taux aux conventions et textes d'application : d'où un barème modifiable.
    — *PAI-03* : `HeureSupplementaire` gagne `Heures60/Montant60` (dimanche/férié de jour) et des taux portés par l'objet
    (`AppliquerBareme`). **Les noms historiques des propriétés et colonnes sont conservés** (Heures30 = tranche 2, majorée à
    35 % depuis octobre 2026 ; Heures100 = férié de nuit, 120 %) pour ne pas migrer les saisies. Taux par défaut = anciens
    littéraux. Lignes de bulletin construites par `HeureSupplementaireService.LignesBulletin` : code et libellé portent le taux
    appliqué (HS35 « Heures supplémentaires 135% ») ; avec le barème historique on retrouve exactement HS15/HS30/HS50/HS100 et
    leurs libellés. Écran HS : colonne « HS dim./férié jour », en-têtes par nature (41e-48e h, au-delà, nuit…) et rappel des
    taux du mois ; export Excel à cinq paires de colonnes. **Preuve d'équivalence** : un dossier v33 créé par l'ancien code
    (HS de septembre et d'octobre 2026, absence manuelle à cheval sur deux mois) puis ouvert par le nouveau : HS de septembre
    identiques au centime (montants, codes, libellés), absences inchangées, octobre aux nouveaux taux.
    — *PAI-04, absences imputées* : `Absences.PeriodeImputation` (« yyyy-MM »). Une absence relevée au planning appartient EN
    ENTIER à son mois de paie (`AbsenceService.JoursPourMoisDePaie`), même si ses dates tombent dans le mois civil précédent
    (arrêté au 20) — y compris quand ce mois précédent est clôturé. `ChargerPeriodeAsync` la liste sous son mois d'imputation
    seulement ; le contrôle de clôture porte sur ce mois ; l'UPDATE de l'écran des congés ne touche pas la colonne ; le
    contrôle « jours > jours ouvrables » ne s'applique pas (un dimanche de rotation est travaillé). Toute absence sans
    imputation suit exactement l'ancienne règle. `RemplacerAbsencesReleveesAsync` remplace en une transaction les absences
    marquées `[RH-08]` du mois pour les salariés concernés ; un salarié dont un relevé chevauche une absence saisie à la main
    est écarté en entier, avec la raison.
    — *Modèle RH-08* (`Models/GestionTemps.cs`) : codes J, N, R, FJ, FN, AJ, A, CP, M ; `MotifCycle` (« 7J 7N 7R », « 4J 4R »,
    « J J N N R R », normalisé et répété depuis `DateDebutCycle`, y compris avant) ; `EquipeTravail` (durées des postes,
    heures normales par poste = 8, semaine civile ou de rotation, dimanche majoré, panier fixe ou fraction du taux horaire) ;
    `PeriodePointage.Pour(annee, mois, jourArrete)` (0 = mois civil ; 20 = du 21 du mois précédent au 20) ;
    `RecapTempsSalarie`, `SemaineDecompte`.
    — *Moteur* (`Services/CalculTemps.cs`, pur, sans base) : J/N un jour férié (ou un dimanche si l'équipe le majore) → FJ/FN ;
    heures = durée du poste ou heures pointées ; AJ = 0 h, ou 8 h si le dossier le décide ; par semaine : normales =
    min(40, total), puis nuit (excédent des nuits au-delà de 8 h), tranche 1 (8 h), férié jour, férié nuit, tranche 2 = solde,
    chacune bornée par ce qui reste. **Une semaine est rattachée à la période qui contient son dernier jour** : décomptée en
    entier une seule fois (jours de la période précédente compris), les derniers jours d'une période étant reportés avec leur
    semaine. Paniers = postes travaillés de la période. Les jours « A » consécutifs deviennent une absence non rémunérée.
    — *Service* (`Services/GestionTempsService.cs`) : équipes, affectations (un salarié = une équipe), jours fériés, paramètres,
    planning (upsert, code vide = suppression, refus si la paie du jour est clôturée), génération depuis les cycles (jamais
    sur un jour saisi/importé sauf « Régénérer » ; jours de la 1re semaine hors période complétés seulement s'ils sont vides ;
    jour de travail couvert par une absence de « Congés et absences » → CP/M/AJ/A), décompte, transferts : HS dans la saisie
    PAI-03 du mois (salariés concernés seulement, commentaire `[RH-08]`), absences imputées, paniers en variables du mois
    (rubrique `PANIER`, lignes `[RH-08]` remplacées).
    — *Import/Export* (`Services/GestionTempsExcelService.cs`) : export du planning en grille colorée (une colonne par jour,
    liste déroulante des codes) + récapitulatif + légende ; réimport de cette grille corrigée ; import de pointages Excel/CSV
    (Matricule, Date, Code ou Entrée/Sortie — nuit si entrée ≥ 18:00 ou < 05:00, sortie le lendemain reconnue —, Heures) ;
    modèle de fichier de pointages.
    — *Écran* (`Views/GestionTempsView.xaml(.cs)`, `ViewModels/GestionTempsViewModel.cs`) : 4 onglets — Planning (colonnes de
    jours construites dans le code-behind à chaque changement de période, saisie au clavier « comme dans Excel », couleurs
    par code, infobulles), Décompte & transfert (KPI, détail par semaine), Équipes & cycles (fiche + affectation par cases à
    cocher), Paramètres, fériés & barème HS. Rechargement à chaque retour sur l'écran (`OnIsActiveChanged`) ; proposition
    d'enregistrer avant de changer de période ou de calculer ; commandes d'écriture réservées à `UserSession.CanEdit`.
    — *Navigation et licence* : bouton « Gestion des temps & rotations » sous RESSOURCES HUMAINES, entrée de recherche
    `GestionTemps`, DataTemplate ; module de licence **RH08** ajouté à `LicenceModules` (fichier partagé : **recompiler aussi
    le générateur de licences** avant d'émettre une licence qui l'inclut).
    — *Tests* (`IUTSCalculator_Tests/GestionTempsTests.cs`, 12 tests) : cas de l'onglet 01, somme des tranches, fériés et
    dimanche majoré, AJ, arrêté et semaines à cheval sans doublon ni oubli, cycle, regroupement des absences, équivalence
    PAI-03, barème applicable, absence imputée, dossier neuf, chaîne complète planning → décompte → transferts.
    — *Dette RH-08* :
    (1) **Plan de paie jamais amorcé dans un dossier neuf** (antérieur à RH-08, découvert en le testant) : les migrations 25
    et 26 insèrent leurs rubriques (RAPPEL*, AVNAT_*) même dans un plan vide, et `RubriqueService.ChargerAsync` n'amorce le
    plan initial que s'il est vide — un dossier créé aujourd'hui n'a donc ni SALBASE ni HSUP/PRIME/AVANCE dans « Plan de
    paie ». **Corrigé le 19 octobre 2026 (migration 36)**, voir plus bas.
    (2) **Ordre d'imputation des HS** : celui du classeur KS (nuit, puis 8 h à 15 %, puis fériés, puis 35 %), et non
    chronologique (les dernières heures de la semaine). Les deux lectures coexistent ; à confirmer avec le conseil du client
    si un contrôle de l'inspection du travail le demande.
    (3) Une absence NON imputée posée seule sur un dimanche compte 0 jour dans PAI-04 (`JoursDansPeriode`, dimanches exclus) ;
    sans effet sur les absences relevées par RH-08, qui sont imputées. **Corrigé le 19 octobre 2026.**
    (4) Pas de remplissage d'une plage de cases en une fois dans la grille (régénérer depuis le cycle couvre l'essentiel).
    **Corrigé le 19 octobre 2026.**
    - **19 octobre 2026 — livraison du module RH-06 « Discipline & procédures ».**
    — *Cadrage juridique* : le Code du travail (loi n° 013-2026/ALP du 6 mai 2026, comme l'ancien 028-2008/AN) pose la
    discipline de l'entreprise comme obligation du travailleur, la mise à pied comme cause de suspension du contrat, la
    rupture sans préavis pour faute lourde sous contrôle du juge, l'absence d'indemnité de licenciement en cas de faute
    lourde, l'obligation de motiver par écrit la rupture (sinon irrégularité indemnisée), l'autorisation préalable de
    l'inspecteur du travail pour licencier un délégué, et le règlement intérieur visé par l'inspection. Il **ne fixe ni
    l'échelle des sanctions, ni la durée maximale de la mise à pied, ni les délais de procédure** : ils relèvent du règlement
    intérieur et de la convention collective. D'où un module paramétrable dont les valeurs par défaut sont indicatives
    (réponse 3 jours ouvrables, convocation 5 jours ouvrables pleins, engagement 60 jours, décision 30 jours, mise à pied 8 j
    au plus). Les lettres ne citent **aucun numéro d'article** (le code vient de changer, la numérotation définitive n'a pas
    pu être vérifiée) : elles renvoient au Code du travail, au règlement intérieur (référence saisie) et à la convention.
    Le client n'a pas pu être consulté sur ces choix (reprise de session) : ils sont réversibles par paramétrage.
    — *Modèle* (`Models/Discipline.cs`) : `ProcedureDisciplinaire` (faits, date de connaissance, qualification légère /
    sérieuse / lourde, salarié protégé et mandat, demande d'explication et réponse, mise à pied conservatoire, convocation,
    entretien (heure et lieu, assistant, compte rendu), décision de l'inspecteur, sanction envisagée puis décidée, motifs,
    dates de mise à pied et jours retenus, date d'effet du licenciement, classement sans suite, notification et mode,
    accusé de réception, recours, liens vers les absences créées). **L'étape (`Statut`) n'est jamais saisie** : elle se déduit
    des dates (ouverte → explication demandée → entretien → décision à notifier → notifiée / classée sans suite).
    `TypeSanction` (code, libellé, nature morale / mise à pied / licenciement / autre, rang, exige explication, exige
    entretien, durée max, faute lourde, effacement en mois, active, système) ; `ParametresDiscipline` ; `PieceDisciplinaire`.
    — *Moteur pur* (`Services/ControleDiscipline.cs`) : jours ouvrables du lundi au samedi, fériés RH-08 exclus ; date limite
    de réponse ; première date d'entretien ; fin d'instruction et date limite de décision ; pièces exigées selon le stade et
    la sanction ; **bloquants** : dates dans le désordre, décision sans demande d'explication (si exigée) ou avant la fin du
    délai de réponse, décision sans entretien (si exigé) ou avant lui, mise à pied sans dates / au-delà du maximum / plus de
    jours que la période / commençant avant la notification, licenciement sans motifs écrits, faute lourde sur une faute non
    qualifiée de lourde, licenciement d'un salarié protégé sans autorisation accordée (ou décidé avant elle), notification sans
    décision, classement sans motif ; **à vérifier** : engagement tardif (prescription), convocation trop proche de
    l'entretien, décision hors délai, décision non notifiée, conservatoire qui s'éternise, pièces manquantes une fois la
    procédure close, mêmes faits déjà sanctionnés (même date, même nature) ; **info** : étape suivante, antécédents non
    effacés, effets paie et SAL-05. Échéancier et registre (effacement = notification + N mois du type).
    — *Service* (`Services/DisciplineService.cs`) : CRUD, référence `DISC-AAAA-NNNN` par année de connaissance des faits,
    refus de tout enregistrement portant une alerte bloquante, salarié non modifiable une fois la procédure créée, suppression
    refusée une fois la sanction notifiée (registre), pièces copiées sous `Pieces\<base>\Discipline\<référence>` à côté de
    la base (retrait refusé après notification), journal d'audit (entité `Discipline`). **Passerelle PAI-04, dans la même
    transaction** : mise à pied notifiée → absence `MiseAPied` (non rémunérée) marquée `[RH-06]`, mise à jour ou retirée si la
    décision change ; mise à pied conservatoire → absence seulement si la décision finale est un licenciement pour **faute
    lourde** (jusqu'à la veille de l'effet), sinon les jours restent payés ; contrôle de clôture des mois touchés et de
    chevauchement avec les autres absences ; une absence inchangée n'est pas réécrite (un recours se saisit après clôture).
    En cas d'échec, rien n'est écrit et l'objet édité retrouve son identifiant, sa référence et ses liens d'origine.
    **Passerelle SAL-05** : `DernierLicenciementNotifieAsync` ; l'écran Solde de tout compte propose le motif
    (`LicenciementFauteLourde` ou `LicenciementPersonnel`) et la date d'effet comme date de sortie, avec un rappel visible.
    — *Lettres et export* (`Services/DisciplineExportService.cs`) : demande d'explication (avec date limite), convocation à
    entretien préalable (sanction envisagée, heure et lieu, droit à assistance), mise à pied conservatoire, notification de
    sanction (motifs, dates et reprise pour une mise à pied), lettre de licenciement (motifs, autorisation de l'inspecteur,
    faute lourde sans préavis ni indemnité ou préavis conventionnel) ; en-tête du dossier, bloc de décharge du salarié et
    des témoins ; chaque lettre est produite dans le répertoire des pièces et versée au dossier. Registre Excel 3 feuilles
    (registre des sanctions, procédures, échéancier).
    — *Écran* (`Views/DisciplineView.xaml`, `ViewModels/DisciplineViewModel.cs`) : KPI, onglets Procédures (liste filtrable +
    fiche en 11 sections avec contrôles en direct en tête), Échéancier, Registre des sanctions (sanctions effacées
    masquables), Délais & catalogue des sanctions. Confirmation explicite avant d'enregistrer une notification ;
    rechargement au retour sur l'écran ; proposition d'enregistrer en quittant une fiche modifiée. Navigation « Discipline &
    procédures » sous RESSOURCES HUMAINES, recherche `Discipline`, module de licence **RH06** (recompiler le générateur).
    — *Vérification* : compilation complète (0 erreur) ; 13 tests NUnit (`IUTSCalculator_Tests/DisciplineTests.cs`) ;
    86 contrôles d'intégration sous Mono sur un dossier **v34 créé par le code RH-08** puis ouvert par le nouveau
    (migration 35 et sauvegarde, absences existantes identiques au centime, mise à pied → retenue 82 000 × 3/30,
    clôture : recours accepté et modification refusée avec annulation complète, chevauchement refusé sans rien écrire,
    salarié protégé, faute lourde et conservatoire (9 jours retenus) puis requalification qui la rend payée, classement
    sans suite, pièces, catalogue, paramètres, dossier neuf) ; les cinq lettres et le registre générés et relus.
    — *Dette RH-06* :
    (1) **Délais et échelle à faire valider** par le client (et son règlement intérieur visé) : valeurs livrées indicatives.
    (2) ~~Le statut « salarié protégé » se coche sur la procédure : il n'existe pas encore de registre des mandats.~~
    **Corrigé le 19 octobre 2026** (registre des salariés protégés, voir plus bas).
    (3) Le licenciement notifié ne sort pas le salarié lui-même : c'est la validation du solde de tout compte (SAL-05) qui le
    fait, motif et date proposés.
    (4) Pas de modèle de lettre modifiable par l'utilisateur (textes dans le code) ; pas d'envoi par courriel. Relève
    d'ETA-06 (« Attestations & courriers types », modèles éditables).
    - **19 octobre 2026 — correction de la dette signalée (RH-08 n°1, 3 et 4 ; RH-06 n°2).** `SchemaCible = 36`.
    — *Plan de paie jamais amorcé* (`SQLiteService.Migration36_PlanDePaieComplet`) : `RubriqueService.ChargerAsync`
    n'amorce le plan que s'il est VIDE, alors que les migrations 11, 25, 26, 27 et 34 y déposent leurs rubriques sans
    condition. Conséquence réelle : dans tout dossier créé depuis PAI-07 (ou dont le plan n'avait jamais été ouvert avant
    ces migrations), le plan ne contenait que RAPPEL*, AVNAT_*, PANIER — ni SALBASE, ni les indemnités, ni HSUP/PRIME/
    AVANCE/PRET… — et une variable du mois saisie sur ces codes était **ignorée par le bulletin** (`ApportVariables.Depuis`
    écarte une rubrique introuvable). Correction : si `SALBASE` manque (signature sûre : une rubrique système ne se supprime
    jamais) et que le plan n'est pas vide, le plan initial complet est inséré en `INSERT OR IGNORE` — une rubrique déjà
    présente, modifiée ou non, est conservée ; un plan sain n'est pas touché (une rubrique supprimée par l'utilisateur ne
    ressuscite pas). Aussi rejouée EN DERNIER dans `CreerSchemaComplet`. Vérifié sur un dossier v34 réellement touché
    (11 rubriques, sans SALBASE) : 20 rubriques ajoutées (7 système, 13 de saisie ; PANIER existait déjà), rubrique PANIER modifiée conservée, variable PRIME de
    nouveau retenue ; dossier sain : 0 ajout ; dossier neuf : plan complet.
    — *Absence d'un dimanche* (`AbsenceService.JoursDansPeriode`, `Anomalie`) : quand le décompte « dimanches exclus » vaut
    zéro (absence posée sur un seul dimanche), repli sur les jours calendaires — elle compte pour le nombre saisi, au plus
    1 ; au-delà, l'enregistrement est refusé. Toutes les autres absences gardent exactement leur décompte (vérifié : absence
    à cheval sur deux mois toujours 2,4 + 1,6). L'écran des congés propose 1 jour pour un dimanche seul, avec explication.
    — *Planning RH-08* : « Remplir la sélection » (code choisi + « Appliquer à la sélection ») sur les cases sélectionnées
    à la souris (cliquer-glisser, Maj/Ctrl), touche Suppr pour vider la sélection ; les cases changent comme une saisie,
    rien n'est écrit avant « Enregistrer » (`GestionTempsViewModel.AppliquerCode`, code-behind `GestionTempsView`).
    Défilement (demande du client, même jour) : barre horizontale toujours affichée sous la grille, Maj + molette,
    boutons « ◀ Début », « Aujourd'hui », « Fin ▶ » (colonnes matricule/salarié/équipe figées). Une grille WPF SANS LIGNE
    n'a pas d'étendue horizontale (rien à faire défiler, même les en-têtes) : un message sur la grille vide explique
    d'affecter les salariés à une équipe puis de générer le planning (`PlanningVide`).
    — *Registre des salariés protégés* (RH-06, table `MandatsRepresentatifs`, onglet « Salariés protégés ») : mandat,
    début, fin (vide = en cours), protection prolongée éventuelle (le Code n'a pas été vérifié sur ce point : champ
    laissé au dossier). `ControleDiscipline.MandatApplicable` : un mandat qui protège le salarié entre la date des faits et
    la décision. Le salarié choisi pour une nouvelle procédure est coché « protégé » automatiquement ; une procédure non
    cochée malgré un mandat est signalée, et **bloquée** si elle aboutit à un licenciement.
    — *Vérification* : compilation complète 0 erreur, liaisons XAML contrôlées ; tests NUnit : 14 RH-06 (dont mandats) +
    3 nouveaux `AbsenceDimancheTests` + 10 RH-08 inchangés ; intégration sous Mono : 61 contrôles des corrections, 86 de
    RH-06 et ceux de RH-08 rejoués (seuls changent le numéro de version et le nom de la sauvegarde, désormais `avant-v36`).
    — *Reste ouvert* : RH-08 n°2 (ordre d'imputation des HS — choix du client), RH-06 n°1 (délais à valider avec le
    règlement intérieur), RH-06 n°3 (voulu) et n°4 (ETA-06).
    - **19 octobre 2026 — décision du client : 8 heures = 1 jour pour les absences (PAI-04).** Défaut relevé pendant les
    corrections : une absence saisie en heures seulement comptait UNE JOURNÉE ENTIÈRE par date dans les bases « trentième »
    et « jours ouvrables » (4 h retenues comme 1 jour) ; au taux horaire, une absence saisie en jours seulement ne donnait
    lieu à AUCUNE retenue, et les heures d'une absence à cheval sur deux mois étaient comptées en entier dans chacun.
    `Absence.HeuresParJour = 8` (constante, pas un paramètre du dossier), `Absence.JoursEquivalents` /
    `HeuresEquivalentes` ; `AbsenceService.JoursDansPeriode` (heures seules → heures ÷ 8), nouveaux `HeuresDansPeriode` /
    `HeuresPourMoisDePaie` (jours seuls → jours × 8, réparties au même prorata que les jours), `Apport` (heures du mois),
    `Solde` (congés pris en heures convertis), absence imputée RH-08 en heures seules ; `Anomalie` refuse plus de 24 h par
    jour calendaire. **Inchangé** : toute absence saisie en jours dans une base en jours, et toute absence en heures d'un
    seul mois au taux horaire (6 tests `AbsenceHeuresTests` ; intégration : 1,6 j + 4 h = 2,1 j → 8 400 FCFA sur
    120 000, et 16,8 h au taux horaire). Au taux horaire, 1 jour = 8 h ÷ 173,33 h du salaire, soit davantage qu'un
    trentième : conséquence voulue de l'équivalence retenue.
    - **19 octobre 2026 — livraison du module RH-07 « Santé & sécurité au travail » (fin de la Phase 4).** `SchemaCible = 37`.
    — *Cadrage* : loi n° 004-2021/AN (sécurité sociale) et site de la CNSS : l'accident du travail se déclare **à la CNSS et à
    l'inspection du travail dans les 48 heures ouvrables**, sur l'imprimé de la CNSS en deux exemplaires ; la victime peut
    déclarer elle-même pendant deux ans ; le **jour de l'accident est entièrement à la charge de l'employeur** ; à partir du
    lendemain, **indemnité journalière égale aux 2/3 de la rémunération journalière**, chaque jour d'incapacité, ouvrable ou
    non ; premiers soins à la charge de l'employeur. Les délais de visites médicales ne sont pas fixés ici par la loi (service
    de santé au travail, convention) : valeurs indicatives paramétrables. **Choix du client** (19 octobre 2026) :
    (a) **salaire maintenu en entier** pendant l'arrêt (comme aujourd'hui) — le module calcule les IJ à réclamer à la CNSS,
    aucun bulletin ne change ; (b) **EPI suivis par dotation individuelle** (catalogue + durée de vie, remises, décharge,
    alertes de renouvellement ; pas de gestion de stock) ; (c) **visites tous les 12 mois, ajustables par poste**, visites
    d'embauche et de reprise suivies.
    — *Modèle* (`Models/SanteSecurite.cs`) : `VisiteMedicale` (type, date de convocation, date de visite, médecin, avis
    d'aptitude, réserves, prochaine visite fixée par le médecin, accident lié), `TypeEPI` (durée de vie en mois, 0 = sans
    échéance, norme, actif), `DotationEPI` (quantité, taille, renouvellement, état en service / rendu / perdu-usé, retour),
    `AccidentTravail` (nature travail / trajet / maladie professionnelle, date, heure, lieu, circonstances, siège et nature
    des lésions, témoins, premiers soins, arrêt et dates, reprise, mortel, déclarations CNSS et inspection, référence CNSS,
    rémunération journalière, taux d'IPP, lien vers l'absence), `ParametresSST`, `PeriodicitePoste`, `EcheanceSST`,
    `SituationMedicale`, `LigneIndemniteJournaliere`, `AlerteSST`.
    — *Moteur pur* (`Services/ControleSanteSecurite.cs`) : rémunération journalière proposée (base + 4 indemnités ÷ 30) ;
    jours indemnisés (du lendemain de l'accident à la fin d'arrêt, sinon veille de la reprise, sinon aujourd'hui ; 0 sans
    arrêt ou accident mortel) ; montant CNSS arrondi et part employeur (le tiers) ; état mensuel des IJ ; date limite de
    déclaration (2 jours ouvrables, lundi-samedi, fériés RH-08 exclus) ; périodicité applicable (poste, sinon dossier) ;
    prochaine visite (sans visite : embauche due à l'entrée ; sinon date fixée par le médecin, sinon dernière + périodicité) ;
    visites de reprise attendues (arrêt AT ou maladie de plus de 21 jours sans visite depuis) ; **échéancier** (visites dues ou
    programmées jusqu'à l'horizon d'alerte, reprises des arrêts terminés depuis moins de 90 jours, EPI à renouveler,
    accidents à déclarer, reprises à saisir) ; situation médicale par salarié actif ; contrôles de saisie. **Bloquants** :
    victime absente, accident dans le futur, arrêt sans premier jour, dates d'arrêt dans le désordre, déclaration avant
    l'accident, IPP hors 0-100 %. **À vérifier** : déclaration tardive ou en retard (CNSS et inspection séparément), fin
    d'arrêt manquante, rémunération journalière manquante, visite de reprise non enregistrée. **Info** : date limite, jour de
    l'accident à l'employeur, IJ estimées, accident mortel.
    — *Service* (`Services/SanteSecuriteService.cs`) : CRUD des trois registres, paramètres et périodicités par poste,
    référence `AT-AAAA-NNNN` par année de l'accident, refus de toute saisie bloquante. **Passerelle PAI-04, dans la même
    transaction** (via `DisciplineService.AppliquerAbsenceAsync`, désormais `internal static` et partagé) : arrêt aux dates
    connues → absence `AccidentTravail` (rémunérée par nature) marquée `[RH-07]`, jours ouvrables (dimanches exclus), mise à
    jour à la prolongation, retirée si l'arrêt l'est ; contrôle de clôture et de chevauchement ; en cas d'échec rien n'est
    écrit et l'accident retrouve identifiant, référence et lien. Un accident **déclaré à la CNSS ne se supprime plus**
    (registre). Journal d'audit (entité `Santé & sécurité`) **sans les réserves médicales**.
    — *Documents* (`Services/SanteSecuriteExportService.cs`) : **fiche de déclaration** A4 d'une page (employeur et n° CNSS,
    victime et n° d'immatriculation, accident, conséquences et IJ estimées, déclarations, date limite en rouge si dépassée,
    rappels CNSS, signature) — préparatoire : la déclaration officielle reste l'imprimé de la CNSS ; **fiche de dotation en
    EPI** (tous les EPI en service du salarié, émargement, engagement, signatures) ; **registre Excel 5 feuilles** (accidents,
    IJ de l'année avec total, visites, EPI remis, échéancier).
    — *Écran* (`Views/SanteSecuriteView.xaml`, `ViewModels/SanteSecuriteViewModel.cs`) : 7 indicateurs ; onglets Échéancier
    (filtres, double-clic : ouvre l'accident ou la remise, ou prépare la convocation pré-remplie), Accidents du travail (liste
    + fiche en 4 sections, contrôles en direct, boutons « Aujourd'hui » pour les déclarations, « Proposer » la rémunération,
    « Programmer la visite de reprise »), Visites médicales (situation par salarié / historique + fiche ; rappel : aucun
    diagnostic), Équipements de protection (remises + fiche, « Renouveler » = rendu aujourd'hui + nouvelle remise), Indemnités
    journalières (année, mois, totaux), Paramètres & catalogue des EPI. Proposition d'enregistrer un accident modifié en
    quittant. Navigation « Santé & sécurité au travail » sous RESSOURCES HUMAINES (icône `IconHealth` ajoutée à `App.xaml`),
    recherche `SanteSecurite`, module de licence **RH07** (recompiler le générateur).
    — *Vérification* : compilation complète 0 erreur, 173 liaisons XAML contrôlées ; 11 tests NUnit
    (`IUTSCalculator_Tests/SanteSecuriteTests.cs`) ; 116 contrôles d'intégration sous Mono : dossier v36 créé par le code
    d'avant RH-07 puis migré (sauvegarde `avant-v37`, absences existantes identiques, pas de doublon à la réouverture), dossier
    v34 migré d'un coup, paramètres (2/3 exacts relus), périodicité par poste, visites, catalogue et remises d'EPI, accident →
    absence de 13 jours puis prolongation à 19 jours sur la même absence, salaire maintenu (aucune retenue), chevauchement et
    période clôturée refusés avec annulation complète, saisie sans effet paie acceptée après clôture, arrêt retiré puis remis,
    suppression, IJ (16 × 3 000 × 2/3 = 32 000), échéancier, dossier neuf ; fiche de déclaration, fiche de dotation et
    registre générés et relus. Tests RH-06 et correctifs rejoués (version attendue désormais 37, sauvegarde `avant-v37`).
    — *Dette RH-07* :
    (1) Pas de pré-remplissage de l'imprimé officiel de la CNSS (sa mise en page n'est pas disponible) : la fiche en
    rassemble les éléments à reporter.
    (2) La rémunération journalière est proposée sur le salaire mensuel ÷ 30 (base + indemnités) ; la CNSS retient la
    rémunération moyenne de la période de référence : montant modifiable par accident, à vérifier sur la notification CNSS.
    (3) Les IJ sont un état de suivi : le remboursement reçu de la CNSS n'est ni saisi ni rapproché (relève d'une évolution
    comptable).
    (4) Un arrêt sans date de fin n'est pas reporté dans les absences tant que la fin n'est pas saisie (alerte « à
    vérifier ») ; une rechute se saisit comme une prolongation ou un nouvel accident.
    (5) Pas de stock d'EPI (choix du client) ; pas de modèle de document modifiable (ETA-06).
    - **19 octobre 2026 — protection des données lors des mises à jour (version 1.4.37.1).** Demandée par le client après
    la question « comment déployer chez les clients sans perte de données ». Constat : programme et données étaient déjà
    séparés et les migrations additives, mais (a) la chaîne de migrations travaillait directement sur le fichier, étape par
    étape, alors que le message d'échec affirmait « aucune donnée n'a été modifiée » ; (b) un ancien programme ouvrait sans
    rien dire une base plus récente ; (c) la version restait à 1.0.0.0 ; (d) aucune sauvegarde / restauration dans le
    programme. — *Corrections* : mise à niveau **tout ou rien** sur copie de travail et refus d'une base plus récente
    (`Services/SQLiteService.cs`, §3) ; ouverture d'un dossier précédée de la préparation de sa base, avec message clair
    (`DossierSelectionViewModel`) ; **numéro de version** (`Properties/AssemblyInfo.cs` 1.4.37.1, `Helpers/VersionApplication.cs`,
    titre, barre d'état, À propos, écran de sélection) ; **sauvegarde / restauration** (`Services/SauvegardeService.cs`,
    `Helpers/SauvegardeInteractive.cs`) : un zip de tout le dossier de données (bases copiées par `VACUUM INTO`, cohérentes
    même programme ouvert ; `.bak`, copies de travail et journaux exclus ; manifeste `sauvegarde-sigma.txt` : date, version,
    poste, bases et niveaux ; base d'un dossier rangée hors du dossier de données signalée), écrit sous un nom provisoire puis
    relu avant de remplacer un fichier existant. **Restauration** : seulement au démarrage, avant d'ouvrir un dossier (le
    bouton est désactivé quand un dossier est ouvert) ; refus d'une archive étrangère, d'une base illisible ou d'une
    sauvegarde faite par une version plus récente ; chemins sortant de l'archive refusés ; **sauvegarde de sécurité
    automatique de l'état actuel** (Documents\Sigma Paie & RH\Sauvegardes\Avant_restauration_…zip) sans laquelle rien
    n'est touché ; fichiers remis en place sans supprimer ceux créés depuis ; anciennes copies `.avant-vN.bak` de la base
    remplacée renommées ; redémarrage du programme (les utilisateurs restaurés s'appliquent). Boutons « Sauvegarder les
    données… » et « Restaurer… » sur l'écran de sélection du dossier, « Sauvegarder les données… » dans le menu ⋯.
    — *Vérification* : compilation complète 0 erreur ; 4 tests NUnit (`IUTSCalculator_Tests/ProtectionDonneesTests.cs`, vraies
    bases dans le dossier temporaire) ; 56 contrôles d'intégration sous Mono (échec de migration : fichier identique à
    l'octet, message exact, copie jetée, réussite après correction ; reste de copie après coupure ; base v38 refusée et
    intacte ; aller-retour sauvegarde / restauration avec pièces, CV, utilisateurs, base v36 restaurée puis mise à niveau ;
    sauvegarde d'une version plus récente, archive étrangère et archive malveillante refusées sans rien modifier) ; suites
    RH-07 (116), RH-06 (86), correctifs (64) et tests unitaires rejoués avec le nouveau mécanisme de mise à niveau.
    — *Reste ouvert* : pas d'installateur (copie du dossier `bin\Release`) ; pas de sauvegarde automatique planifiée ; la
    licence (liée au poste) ne fait pas partie des sauvegardes ; le logo d'un dossier (chemin libre) non plus.

    - **21 octobre 2026 — correction des 25 défauts relevés en préparant le guide de formation (version 1.4.38.0, base v38).**
    Demandée par le client (« lance les corrections ») après la section 15.5 du guide ; choix du client : tous les défauts,
    la paie d'abord ; le solde de tout compte produit un **bulletin de sortie** ; ancienneté au **1er du mois de paie**
    partout ; écritures au **format Sage 100c** avec comptes réglables. — *Paie* : chaîne d'assemblage unique
    (`Services/ChaineBulletinService.cs`) utilisée par Bulletin, plage de mois, Calcul en lot, Calcul d'IUTS et STC (saisies,
    rappels, archive ; jamais de remplacement silencieux d'un bulletin archivé ; un bulletin de sortie validé ne peut plus
    être écrasé par une paie ordinaire) ; date de calcul de l'ancienneté unique (`ConventionCollectiveService.DateCalculAnciennete`) ;
    « Cadre Supérieur » et « Cadres Supérieurs » ont tous deux l'abattement de 20 % (`Employee.EstCategorieCadreSuperieur`), liste
    de catégories éditable qui n'écrase plus une valeur enregistrée ; saisies sur salaire : date d'effet respectée, libellé
    lisible, bordereau du mois choisi, aperçu recalculable ; mode « Mobile money » et « Mode de la fiche » ; rappels refusés
    sur un mois clôturé, imputés par défaut sur la période ouverte, Δ IUTS estimé, et repris au recalcul d'un mois ;
    **prorata automatique** des entrées et sorties en cours de mois (base, indemnités, prime d'ancienneté, gains permanents
    du profil, abattement forfaitaire et plafond retraite sur la base due) ; garde-fou HSUP / module HS, code `HS50J` au barème
    2000 ; frais de mission et remboursements hors TPA ; reprise du solde de congés (« Congés repris (j) », « Reprise au ») ;
    préavis 3 mois cadres, agents de maîtrise et techniciens, 1 mois sinon, modifiable (source à confirmer, voir dette) ;
    départ volontaire à la retraite : indemnité réduite de moitié ; réimport CSV qui conserve poste, manager, reprise de
    congés et type de contrat, statuts « Sorti… » / « Suspendu… » reconnus. — *Modules RH* : RH-08 absence « A » comptée au
    poste prévu par le cycle (0 h sur un repos) et pré-chargement des 11 jours chômés de la loi n° 001-2026/ALT (fêtes
    musulmanes 2026 seulement, sourcées ; lundi de Pâques supprimé par cette loi) ; RH-07 prolongation d'un arrêt touchant un
    mois clôturé, suivi des remboursements d'IJ par la CNSS ; RH-04 totaux du plan non multipliés par les présents, filtres
    « Statut » ; RH-03 validation en une seule transition, date et auteur scellés ; RH-05 matricule selon le schéma du
    dossier, contrat, motif, convention, échelon, catégorie et indemnités repris à l'embauche ; RH-02 niveaux 1 à 4
    corrects (anciennes lignes « à vérifier »), convention enregistrée, minimum repris de la grille ; RH-01 codes uniques à
    l'initialisation (transaction unique), filtre des postes ; RH-06 procédure notifiée figée, « Salarié protégé » recalculé ;
    simulateur des conventions branché. — *Contrôle* : profil « Lecture seule » appliqué par la base elle-même
    (`Services/GardeLectureSeule.cs`, autorisateur SQLite posé par `SQLiteService.CreateConnection` après la mise à niveau ;
    seul le journal d'audit reste inscriptible ; bandeau « LECTURE SEULE » ; création, suppression de dossier et restauration
    interdites) ; « Clôturée par » = utilisateur connecté ; écritures : comptes réglables par dossier, longueur uniforme,
    export « Import Sage 100c » (JO_Num;EC_Reference;Date;EC_Piece;CG_Num;CT_Num;EC_Intitule;Sens;Montant, Windows-1252,
    intitulé ≤ 33 car., pièce équilibrée sinon refus), journal par défaut « PAIE » ; journal d'audit paginé sans plafond,
    barèmes et calculs journalisés, chaînage SHA-256 et « Vérifier l'intégrité ». — *Vérification* : compilation complète
    0 erreur ; migration v37 → v38 et base neuve testées (idempotence, données conservées) ; tests du client mis à jour et
    nouveaux tests (`CorrectionsPaieTests`, `EcriturePaieTests`, `AuditIntegriteTests`, compléments RH) passés sous Mono, sauf
    ceux qui exigent iTextSharp, ClosedXML ou la bibliothèque SQLite native (à rejouer dans Visual Studio) ; année STI rejouée :
    seuls changent STI028 en septembre (prorata de sortie) et STI035 en novembre (indemnité de retraite réduite de moitié) ;
    relecture du code par deux agents, 10 défauts trouvés et corrigés. — *Dette* :
    (1) Préavis : la règle retenue (3 mois cadres, agents de maîtrise et techniciens ; 1 mois sinon) vient d'une source
    secondaire incohérente — à confirmer sur la convention CCI-BF et le Code du travail ; 8 jours des horaires à saisir à la main.
    (2) Pour une absence ordinaire (non rémunérée), l'abattement forfaitaire reste calculé sur le salaire de base complet
    (comportement validé antérieurement) ; seul le prorata d'entrée/sortie le réduit.
    (3) ~~Supprimer un STC ne supprime pas son bulletin de sortie.~~ **Résolu le 26 octobre 2026** (voir l'entrée de cette date).
    (4) Fêtes musulmanes 2027 et suivantes à saisir à la main ; Tabaski 2026 sourcée par la FAIB seulement.
    (5) La garde « Lecture seule » affiche un message traduit dans la barre d'état ; une boîte de dialogue qui affiche
    `ex.Message` brut peut encore montrer « not authorized ». Depuis le 26 octobre, le message traduit s'affiche aussi en
    notification (`BaseViewModel.StatusMessage`), mais les boîtes de dialogue qui affichent `ex.Message` brut restent à reprendre.
    (6) Limites Sage supposées (journal 6, pièce 13, référence 17, compte 13) : à confirmer sur le poste du client.

    - **22 octobre 2026 — gestion des utilisateurs : la sélection remplit enfin la fiche (version 1.4.38.1).** Signalé
    par le client : « quand je clique sur un utilisateur, aucune information n'est saisie dans le formulaire ». Cause : la
    fenêtre avait deux panneaux empilés, « Nouvel utilisateur » (jamais rempli par la sélection) puis « Utilisateur
    sélectionné », coupé par le bas de la fenêtre (560 px, sans défilement) ; le nom affiché et l'identifiant ne se
    modifiaient d'ailleurs nulle part. — *Correction* (`Views/UserManagementWindow.xaml(.cs)`) : une seule fiche, qui
    défile, pour créer ou modifier ; sélectionner une ligne la remplit (nom affiché, identifiant, rôle) et la passe en
    « Modifier l'utilisateur » ; « Nouvel utilisateur » la vide ; mot de passe facultatif en modification (vide = conservé)
    avec confirmation ; « Supprimer cet utilisateur » visible en modification seulement ; la liste resélectionne le compte
    après chaque enregistrement. Nouveau `UserAccountService.ModifierUtilisateurAsync` (identifiant obligatoire et unique sans
    tenir compte des majuscules, mot de passe de 6 caractères au moins, toujours un administrateur, rien d'enregistré en
    cas de refus) ; retirer son propre rôle d'administrateur demande confirmation ; modifier son propre compte met à jour la
    session et le menu « Connecté : … » (`MainWindow.xaml.cs`). — *Vérification* : compilation complète 0 erreur ; 13
    contrôles du service sous Mono (modification, doublon, dernier administrateur, mot de passe conservé / changé / trop
    court, identifiant vide).

    - **26 octobre 2026 — défauts métier restants, ergonomie et sécurité (version 1.4.38.2).** Aucun changement de schéma
    (`SchemaCible` reste à 38).
      1. *Recours favorable — annulation de la retenue* (RH-06). Nouveau bouton « Recours favorable : annuler la retenue »
         (section 9 de l'écran Discipline, `DisciplineViewModel.AnnulerRetenueCommand`) ; actif seulement pour une mise à pied
         NOTIFIÉE dont la date de recours est enregistrée et dont la retenue n'est pas déjà annulée. Il appelle
         `DisciplineService.AnnulerPenalitesRecoursAsync`, en une transaction : l'absence « Mise à pied » liée est retirée de
         PAI-04 par `AppliquerAbsenceAsync` (refus si le mois de paie est clôturé, comme pour toute correction), le lien
         `AbsenceMiseAPiedId` est vidé, les jours retenus passent à 0 (les dates restent au registre, et une procédure
         notifiée ne recrée donc plus d'absence), et l'issue du recours reçoit la mention « Recours favorable : retenue
         annulée le … » ; journal d'audit. La sanction reste au registre, le statut « Notifiée » ne change pas. La mise à pied
         conservatoire d'un licenciement pour faute lourde n'est pas concernée. *Limite* : l'issue du recours est un texte
         libre, rien n'indique encore « favorable » de façon structurée — c'est l'utilisateur qui déclenche l'annulation.
         Une fois les jours à 0, l'écran signale la règle « indiquez le nombre de jours » comme alerte déjà présente sur la
         version notifiée, ce qui n'empêche pas d'enregistrer un accusé de réception ou un commentaire.
      2. *Prorata du 13ᵉ mois au solde de tout compte* (SAL-05). `SoldeToutCompteService.MoisPresence13emeMois` (mois de
         30 jours, du 1er janvier — ou de l'entrée si elle est la même année — à la sortie incluse, plafonné à 12 :
         du 1er janvier au 15 mars = 2,5) et `Calculer13emeMoisProrata` (salaire de référence ÷ 12 × mois de présence).
         **Optionnel** : case « Prorata de l'année si vide » à côté de « Gratification 13e » ; une gratification saisie
         l'emporte ; sans la case, le comportement d'avant est inchangé (0) — tous les employeurs ne versent pas de 13ᵉ mois.
         Le bulletin de sortie reprend ce montant par la rubrique `GRATIF`. 5 tests ajoutés (`SoldeToutCompteTests`).
      3. *Suppression d'un solde de tout compte* : supprime aussi le bulletin de sortie du mois de sortie et remet le
         salarié « Actif » (date et motif de sortie effacés) — dette (3) du §7 levée. *Limite* : le bulletin est retrouvé
         par mois de calcul et salarié ; non rejoué sur un dossier réel.
      4. *Rappels* : `RappelPaieService.ObtenirRappelDoublonAsync` refuse la création d'un rappel identique (même salarié,
         type, montant brut et mois d'origine, hors statut 2).
      5. *Ergonomie / sécurité* : confirmation avant de supprimer un calcul de l'historique ; `NotificationService` (non
         bloquant) remplace plusieurs `MessageBox` (menu, sauvegarde / restauration) ; message « lecture seule » traduit aussi
         en notification ; `users.json` chiffré par DPAPI et injection de formules Excel neutralisée (déjà commités).
      6. *Abattement professionnel — modification TENTÉE PUIS RETIRÉE.* Calculer l'abattement sur la rémunération permanente
         (après absences) au lieu du salaire de base a été essayé : il appliquait le prorata d'entrée/sortie deux fois
         (8 333 au lieu de 25 000 pour une entrée à mi-mois) et contredisait le comportement validé par le client et couvert par
         `CorrectionsPaieTests.AbsenceOrdinaire_AbattementInchange` (75 000 → 62 500). Le moteur est revenu à l'état commité.
         *À trancher avec le client* : si la règle voulue est bien l'abattement sur la rémunération permanente, il faudra
         changer ce test et la dette (2) ci-dessus, et ne prorater qu'une fois.
      7. *Vérification* : compilation complète du projet et des tests par MSBuild (Visual Studio 2022), 0 erreur — elle a
         révélé quatre erreurs de modifications jamais compilées (méthode `EnregistrerRappelAsync` dupliquée dans
         `RappelPaieService.cs`, `using` et appel de `MessageBox` dans `HistoryViewModel`, deux membres inexistants dans
         `SoldeToutCompteViewModel`), corrigées. Suite complète : **196 / 197** tests réussis. Seul échec :
         `ProtectionDonneesTests.MiseANiveauEnEchec_LaisseLeFichierIntact`, **antérieur à ces changements** (le test pose son
         obstacle sur la table `AccidentsTravail`, que la migration 38 idempotente ne bloque plus) — à réécrire avec un
         autre obstacle. Non testés en conditions réelles : le bouton d'annulation, la suppression d'un solde et la case du
         13ᵉ mois (seuls les calculs sont couverts par des tests).

---

## 8. Pour reprendre dans une nouvelle conversation

### Mettre à jour un client sans perte de données

Le programme (dossier d'installation) et les données (`%APPDATA%\IUTSCalculator` : `dossiers.json`, `users.json`,
`settings.json`, `Dossiers\*.db`, pièces, CV ; licence à part dans `%PROGRAMDATA%\SigmaPaieRH`) sont séparés.
1. Chez le client, avant tout : « Sauvegarder les données… » (écran de sélection du dossier ou menu ⋯), copie du zip hors du poste.
2. Fermer le programme ; remplacer TOUT le contenu du dossier d'installation par `bin\Release` (jamais de `.db`).
3. Nouveaux modules achetés : nouvelle licence (générateur recompilé), importée par le client.
4. Relancer, ouvrir chaque dossier (mise à niveau tout ou rien, `.avant-vN.bak` déposé), contrôler quelques chiffres.
5. En cas de problème : remettre l'ancien programme ET « Restaurer… » la sauvegarde de l'étape 1, au démarrage.


Ouvrir la conversation sur le dossier du projet et dire, en substance :

> On poursuit la feuille de route de Sigma Paie & RH. Lis d'abord
> `ETAT-DEVELOPPEMENT.md` à la racine du projet, puis attaque le module suivant.

La feuille de route détaillée des 53 modules est un document séparé ; si elle n'est pas
sous la main, ce fichier suffit à savoir quoi faire ensuite.

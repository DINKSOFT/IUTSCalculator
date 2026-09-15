# Sigma Paie & RH — état du développement

Ce fichier sert à **reprendre le travail dans une nouvelle conversation** sans repartir de
zéro. Il dit où en est le projet, quelles décisions ont été prises et pourquoi, et ce qui
reste en dette. Il se met à jour à chaque module livré.

Dernière mise à jour : 14 septembre 2026 — après **PAI-06** (« Saisies-arrêts, cessions sur salaire et oppositions » :
calcul par tranches progressives de la quotité saisissable et cessible du salaire conforme au Code du travail burkinabè
Loi 028-2008/AN Art. 122+ et OHADA AUPSRVE Art. 177+, protection absolue du SMIG vital (45 000 FCFA), super-privilège
de la pension alimentaire mordant sur la fraction insaisissable au-delà du SMIG, concours de créanciers avec répartition
proportionnelle au marc le franc, suivi des tiers bénéficiaires, des créances et des soldes, arrêt automatique des prélèvements
à l'apurement complet ou sur mainlevée judiciaire, intégration au bulletin individuel et à la génération en masse, et
double export Excel ClosedXML du bordereau mensuel de reversement et du registre général des créances).
La Phase 4 (« Pilotage, automatisation et conventionnel ») progresse à plein régime.

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

### Livré (Phases 1, 2 et 3 intégralement achevées, Phase 4 en cours)

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
| PAI-03 | Heures supplémentaires et majorations légales (saisie mensuelle tranches 115%, 130%, 150%, 200%, valorisation automatique, intégration bulletin/masse, export Excel) |
| SOC-05 | Grilles de salaires et conventions collectives (classification catégorielle, salaires minima garantis, prime d'ancienneté légale CCI-BF, alertes conformité, intégration bulletin) |
| SAL-05 | Solde de tout compte et gestion des départs (décompte de rupture légal burkinabè, indemnités préavis et licenciement, apurement prêts, reçus PDF/Excel, certificat Art. 67, attestation de service) |
| PAI-06 | Saisies-arrêts, cessions et oppositions (calcul par tranches de la quotité saisissable, super-privilège alimentaire, répartition au marc le franc, arrêt auto, exports Excel) |
| PAI-07 | Rappels & régularisations (salaires rétroactifs, primes, trop-perçus, assistant de calcul différentiel, régularisation annuelle d'IUTS de fin d'exercice, inviolabilité des périodes closes, double export ClosedXML) |

### Prochain

**Phase 4 — Pilotage, automatisation et conventionnel.**
Prochain module :
1. **PAI-08** : Avantages en nature (évaluation forfaitaire ou réelle du logement, véhicule, domesticité, eau, électricité, nourriture selon la réglementation fiscale burkinabè, réintégration dans la base IUTS/CNSS/TPA, puis déduction en bas de bulletin du net à payer).

---

## 3. Schéma de base

`SQLiteService.SchemaCible` vaut **25**. Chaque montée de version fait une sauvegarde
`.avant-vN.bak` à côté du fichier avant de migrer.

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

**Bases vierges.** `CreerSchemaComplet` rejoue désormais les migrations qui CRÉENT quelque
chose (12, 13, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25). Un dossier neuf est enregistré directement au niveau cible et
saute toute la chaîne : il naît avec toutes les tables nécessaires (`Absences`, `Prets`,
`ProfilsPaie`, `JournalAudit`, `HeuresSupplementaires`, `ConventionsCollectives`, `SoldesToutCompte`, `SaisiesOppositions`, etc.).

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

## 5. Méthode de travail

- **Équivalence stricte.** Toute modification du moteur de calcul audité ship avec des
  valeurs par défaut identiques aux littéraux qu'elle remplace : un dossier sans données
  nouvelles produit un résultat inchangé, au centime. L'écran **« Contrôle du moteur »**
  le vérifie en rejouant chaque bulletin avec l'ancienne formule et la nouvelle, sans
  aucune tolérance d'arrondi. C'est la preuve, pas la promesse.
- **`device_bash` est inutilisable sur ce poste** (depuis une mise à jour Windows du
  8 septembre 2026). Le déploiement passe par : édition locale → validation → copie dans
  `/mnt/user-data/outputs/` → `SendUserFile` → `device_list_dir` pour relever les mtimes →
  `device_commit_files` avec `expectedMtimeMs`.
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

---

## 7. Dette connue

À traiter, documentée dans le code aux endroits concernés :

1. `employee.PensionContribution = calculation.CnssEmployee` — le moteur **mute la fiche
   du salarié** pendant le calcul. Report historique conservé parce que le déloger
   changerait la base imposable (article 111). À reprendre avec précaution.
2. Les **plafonds de l'article 106** (logement 20 %/75 000, transport 5 %/30 000, fonction
   5 %/50 000) et les abattements professionnels viennent encore de `TaxDataService`, pas
   des barèmes datés de SOC-02.
3. La **migration 7** n'a pas rétro-alimenté la ventilation CNSS des bulletins antérieurs.
4. Les **cumuls annuels** du bulletin sont *estimés* (rémunération du mois × mois écoulés),
   faute d'un historique mensuel complet. Le bulletin l'indique.
5. Les écrans **« Estimation du salaire »** et **« Estimation en lot »** restent des
   simulateurs : ils n'écrivent rien dans l'historique. C'est voulu.
6. Le **numéro d'employeur CNSS** du dossier doit être renseigné, sans quoi la Caisse
   rejette le bordereau. L'écran le signale.
7. Les lignes de remboursement de prêt ne portent **aucun compte général**. CPT-01 devra
   leur en attribuer un — la contrepartie SYSCOHADA d'une avance au personnel — et ce
   choix doit être arrêté avec le comptable, pas deviné ici.
8. ~~La **quotité cessible et saisissable** du salaire n'est pas contrôlée~~
   **Résolu le 14 septembre 2026 par PAI-06** : calcul rigoureux par tranches progressives de la quotité
   saisissable et cessible sur le salaire net de référence ($R_{net} = \text{Brut} - \text{CNSS 5,5\%} - \text{IUTS}$),
   sanctuaire vital du SMIG (45 000 FCFA), super-privilège alimentaire et répartition au marc le franc.
9. Les dossiers qui géraient leurs remboursements par une **retenue saisie à la main** dans
   les variables du mois doivent la supprimer en basculant sur ce module, sinon la retenue
   sera opérée deux fois. Rien ne le détecte automatiquement.
10. Une ligne de profil dont la rubrique est en mode **« base × taux »** n'est pas
    valorisée : résoudre une base qui est elle-même une autre rubrique relève du moteur.
    Ces lignes sont signalées comme « non valorisées » sur l'écran des profils.
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
14. **Aucun verrou n'empêche de générer et d'enregistrer un bulletin sur une période qui
    n'a jamais été ouverte.** `PeriodePaieService.EstClotureeAsync` ne teste que la
    clôture (`Statut = 1`) ; un mois sans aucune ligne dans `PeriodesPaie` compte comme
    « non clôturé », donc traité comme ouvert par défaut. Les quatre écrans qui produisent
    une paie (Bulletin, Bulletins en masse, Calcul d'IUTS, Calcul en lot) n'appellent
    `PeriodePaieService` à aucun moment — seules la saisie des variables du mois et celle
    des absences vérifient la clôture, jamais l'ouverture. Signalé par le client le
    13 septembre 2026. C'est un contrôle de sécurité à ajouter plutôt qu'un module en soi ;
    à rattacher à **PAI-01** (périodes de paie et clôture) pour exiger une ligne `Ouverte`
    avant tout calcul ou toute saisie.
15. ~~**Aucune gestion de sortie d'un employé (fin de contrat, démission).**~~
    **Pris en charge et résolu dans SAL-05 (Solde de tout compte)** : ajout des champs
    `DateSortie`, `MotifDepart`, `StatutSalarie` (Actif / Sorti) dans `Employees` et l'IHM,
    avec calcul des indemnités de rupture et édition des documents légaux.

---

## 8. Pour reprendre dans une nouvelle conversation

Ouvrir la conversation sur le dossier du projet et dire, en substance :

> On poursuit la feuille de route de Sigma Paie & RH. Lis d'abord
> `ETAT-DEVELOPPEMENT.md` à la racine du projet, puis attaque le module suivant.

La feuille de route détaillée des 53 modules est un document séparé ; si elle n'est pas
sous la main, ce fichier suffit à savoir quoi faire ensuite.

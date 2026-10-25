# Analyse du plan RH-02 « Postes & fiches de poste » à la lumière de l'existant

Ce plan a été écrit avec une bonne connaissance de contexte (il cite RH-01, la feuille de route, le schéma cible actuel), mais il a été rédigé sans relire le code réel de RH-01 tel qu'il a été livré le 17 septembre. Résultat : les grandes lignes tiennent, mais deux points structurants entrent en conflit avec ce qui existe déjà, et plusieurs détails techniques ne correspondent pas exactement aux noms réels du code. Rien de tout ça n'est bloquant — mais mieux vaut le trancher avant d'écrire une ligne de C#, plutôt qu'au milieu de l'implémentation.

Deux décisions structurantes ont déjà été prises avec toi pendant cette analyse (détaillées en section 1 et 2) ; le reste du document liste ce qu'il faut corriger dans le plan pour qu'il colle au code réel.

---

## 1. Le vrai problème du plan : la cardinalité Poste ↔ Salarié

**Ce que RH-01 a construit** (`Models/Organigramme.cs`, migration 29) : un `Poste` est une case d'organigramme à **un seul occupant**. Le lien se fait par un champ `TitulaireMatricule` porté directement sur la table `Postes`, et deux propriétés calculées binaires : `EstPourvu` (vrai si `TitulaireMatricule` est renseigné) et `StatutOccupation` (« Pourvu » / « Vacant »). L'écran Organigramme (onglet « Affectation rapide ») écrit cette affectation à deux endroits en même temps : `Postes.TitulaireMatricule` ET `Employees.PosteId` — mais rien n'empêche techniquement deux salariés d'avoir le même `PosteId` ; simplement, le champ `TitulaireMatricule` du Poste ne peut en refléter qu'un seul à la fois.

**Ce que le plan RH-02 suppose** : qu'un poste puisse être « en sureffectif » (§3, `SynthesePosteBudget.Statut`), ce qui n'a de sens que si **plusieurs salariés peuvent légitimement partager un même poste** (ex. « Comptable » avec un effectif budgété de 3). C'est le modèle GPEC classique — un poste est un intitulé de fonction avec une capacité, pas une case à un seul titulaire.

Ces deux modèles sont incompatibles tels quels. Le plan ne le signale nulle part ; il propose simplement d'ajouter `EffectifPourvu`/`EcartEffectif` à côté de l'existant, comme si c'était une extension neutre — ça ne l'est pas.

**Décision prise avec toi : faire évoluer `Poste` vers le modèle GPEC.** Conséquences concrètes à ajouter au plan (absentes de la version actuelle) :
- Le décompte de l'effectif pourvu se fait désormais **exclusivement** par `COUNT(*) FROM Employees WHERE PosteId = @id AND StatutSalarie = 0` (salariés actifs), plus jamais via `Postes.TitulaireMatricule`.
- `Postes.TitulaireMatricule`/`TitulaireNom` ne disparaissent pas de la table (coût de migration inutile) mais changent de sens : ils deviennent un simple cache informatif (« dernier salarié affecté »), non fiable pour un compte exact. `EstPourvu`/`StatutOccupation` doivent être recalculés depuis le décompte réel, pas depuis ce champ.
- **Effet de bord non scopé par le plan** : l'arbre hiérarchique de RH-01 (`NoeudOrganigramme`, onglet 0 « Arbre interactif ») affiche aujourd'hui un seul avatar/badge par poste (`InitialesTitulaire`, calculé à partir d'un seul `TitulaireNom`). Avec plusieurs titulaires possibles, ces cases doivent afficher soit une liste, soit un badge « 2/3 », et la palette de statuts doit passer de 2 états (Pourvu/Vacant) à 3 (Vacant/Pourvu/Sureffectif — reprenant les couleurs déjà prévues par le plan pour l'onglet Budget). Il faut aussi que `NoeudOrganigramme.EffectifDirect`/`EffectifTotal` (aujourd'hui un seul chiffre par nœud, calculé récursivement) portent désormais **budgété ET pourvu** si on veut le taux d'occupation par direction/service que le plan demande explicitement (§1.3 : « Taux d'occupation des postes par direction/service et au niveau global entreprise »). Ce n'est pas une ligne du plan actuel — c'est un vrai chantier dans `OrganigrammeService.cs` (les méthodes qui construisent l'arbre) en plus de tout ce que RH-02 ajoute par ailleurs.

Ce point mérite d'être écrit noir sur blanc dans le plan révisé, avec son propre item de `Proposed Changes` (modification d'`OrganigrammeService`/`NoeudOrganigramme`, pas seulement de `FichePosteService`).

---

## 2. Le deuxième problème : un ou deux éditeurs de poste ?

RH-01 a déjà livré un onglet complet « Référentiel des Postes & Fonctions » dans l'écran Organigramme : CRUD du poste (code, intitulé, service, N+1, effectif), avec anti-cycle. Le plan RH-02 propose un **nouvel écran séparé** (`FichePosteView`, onglet 1 « Fiches de poste ») avec sa propre sélection latérale des postes et son propre formulaire d'édition complet. Si les deux sont construits tels quels, tu te retrouves avec deux endroits pour éditer le même poste — code et intitulé modifiables à deux endroits différents, risque de désynchronisation, et une confusion pour l'utilisatrice finale (« je l'ai modifié où, déjà ? »).

**Décision prise avec toi : RH-02 enrichit l'onglet existant plutôt que de dupliquer l'éditeur.** Concrètement, ça change le périmètre de `FichePosteView.xaml`/`FichePosteViewModel.cs` proposé par le plan :
- L'onglet « Référentiel des Postes & Fonctions » de `OrganigrammeView.xaml` s'enrichit de nouvelles sections (Missions détaillées, Profil de compétences, Classification conventionnelle, Compétences requises avec jauge 1-4, Titulaires actuels — maintenant une liste, cf. §1) : ces champs supplémentaires vivent là où le poste est déjà édité, pas ailleurs.
- Le nouvel écran/module RH-02 ne porte plus que les vues **transverses**, qui n'ont pas leur place dans un formulaire d'édition unitaire : le tableau de bord « Budget vs Pourvu » (tous les postes, tous les écarts, KPI globaux) et le référentiel des compétences (CRUD du catalogue `Competences`, indépendant de tout poste particulier). C'est cohérent avec les onglets 1 et 2 du plan (`FichePosteViewModel`), qui restent valides tels quels — c'est l'onglet 0 (« Fiches de poste », l'édition unitaire) qui disparaît du nouvel écran et migre dans l'existant.
- Ça simplifie d'ailleurs le plan : plus besoin de dupliquer la sélection latérale des postes, la recherche multi-critères ou les filtres par direction/service décrits en §1.6 — ils existent déjà sur l'onglet Organigramme.

---

## 3. Écarts de nommage à corriger avant de coder

Le plan a été écrit sans relire le code final de RH-01 dans le détail ; plusieurs noms ne correspondent pas exactement :

| Le plan écrit | Le code réel (RH-01) | Impact |
|---|---|---|
| `SuperviseurPosteId` | `SuperieurPosteId` | Cosmétique — juste renommer dans la fiche technique avant de générer du code, sinon ça ne compile pas contre le vrai modèle. |
| `Poste.EffectifBudgete` (« nouvelle colonne, synonyme/mapping sur `EffectifRequis` ») | `EffectifRequis` existe déjà (int, défaut 1) | Bon réflexe du plan (pas de doublon voulu) — mais autant l'écrire explicitement : **aucune nouvelle colonne**, on réutilise `EffectifRequis` tel quel et on l'affiche sous le libellé « Effectif budgété » côté UI. |
| `Poste.CategorieConventionnelle` / `EchelonConventionnel` / `SalaireMinimumConventionnel` (nouvelles colonnes proposées) | La grille SOC-05 existante (`GrilleSalaire.cs`) a déjà `ConventionId`, `Categorie`, `Echelon`, `SalaireMinimum` | Pas une erreur de conception, mais un nommage à harmoniser — et surtout un vrai problème de fond, voir §4 ci-dessous. |
| `Poste.Missions` (le plan semble vouloir une nouvelle structure « Finalité générale & missions principales ») | `Poste.Missions` existe déjà (texte libre) | À clarifier dans le plan : le champ existant `Missions` devient-il « missions principales » (réutilisé), avec seulement `FinalitePrincipale` et `ActivitesCles` en vrais ajouts ? Ou bien `Missions` est-il remplacé ? Le plan actuel ne le dit pas. |

---

## 4. Risque réel : double source de vérité sur la classification

`Employee` a **déjà** ses propres champs `Category`, `ConventionId`, `Echelon` — ce sont eux que lit le moteur de paie/IUTS pour tout salarié (confirmé dans le moteur de calcul et l'import CSV). Le plan propose d'ajouter `ConventionId`, `CategorieConventionnelle`, `EchelonConventionnel` **sur le Poste aussi**. Si les deux existent sans règle claire, deux salariés du même poste pourraient afficher une catégorie différente de celle du poste, et rien ne dirait laquelle fait foi pour la paie.

Deux résolutions possibles, à trancher avant de coder (je n'ai pas tranché celle-ci avec toi, elle est moins bloquante que les deux précédentes mais mérite ta décision) :
- **A. Le poste est indicatif seulement** : `CategorieConventionnelle`/`EchelonConventionnel` sur `Poste` servent uniquement à calculer le salaire minimum/la fourchette budgétaire du poste (aide au recrutement), sans jamais écraser ou contraindre le `Category`/`Echelon` propre au salarié. Le plus simple, le moins risqué.
- **B. Le poste fait autorité** : affecter un salarié à un poste met à jour son `Category`/`ConventionId`/`Echelon` automatiquement. Plus cohérent sur le papier, mais ça touche au moteur de paie existant — à ne faire qu'avec une confirmation explicite, comme pour toute modification touchant à l'IUTS.

Je recommande A par défaut, sauf si tu veux spécifiquement que le poste pilote la classification du salarié.

---

## 5. Ce que le plan a bien vu

Pour équilibrer : plusieurs choix du plan sont corrects et bien informés une fois vérifiés contre le code réel.
- **Le calcul du prochain numéro de migration (30) est juste** : `SchemaCible` vaut bien 29 aujourd'hui (RH-01 vient de le monter de 27 à 29 — au passage, la section §3 du fichier ETAT-DEVELOPPEMENT.md elle-même n'a pas été mise à jour depuis un moment et affiche encore 27 : à corriger en même temps que RH-02).
- **Le pattern de navigation** (`NavigateToFichePosteCommand`, `IsNavFichePosteVisible`, indexation dans le moteur de recherche du menu) correspond exactement à la convention réelle (`NavigateToOrganigrammeCommand` / `IsNavOrganigrammeVisible => IsItemVisible("Organigramme")` / entrée dans `_navMenuItems`) — rien à changer là.
- **Le rapprochement conventionnel** (auto-suggérer un salaire minimum depuis la grille SOC-05) est une bonne idée et techniquement faisable : `GrilleSalaire` a déjà tout ce qu'il faut (`ConventionId`, `Categorie`, `Echelon`, `SalaireMinimum`) — aucune méthode de résolution catégorie/échelon → salaire minimum n'existe encore côté service, donc `FichePosteService` devra l'écrire, mais rien ne s'y oppose.
- **Les deux exports (PDF officiel + Excel 3 feuilles)** suivent exactement le pattern déjà établi par RH-01 (`OrganigrammeExportService`) et une dizaine d'autres modules — aucune surprise attendue ici.
- **Le principe anti-obsolescence** (§2, décompte des effectifs directement sur les salariés actifs) est le bon réflexe — c'est very exactement la même classe de bug que celle corrigée sur plusieurs écrans le 16 septembre (items 18-21 du journal).

---

## 6. Point mineur : les tests unitaires proposés

Le plan propose un fichier `FichePosteTests.cs` dans `IUTSCalculator_Tests`. Ce projet existe bien dans la solution (`IUTSCalculator_Tests.csproj`), mais à ma connaissance aucun des ~26 modules livrés jusqu'ici n'y a ajouté de test réel — il ne contient qu'un fichier squelette d'origine (`MainViewModelTests.cs`, jamais retouché). Ce n'est pas un problème en soi, juste un changement de pratique : si tu veux que RH-02 soit le premier module à avoir une vraie couverture de tests, dis-le explicitement, sinon je resterai sur la méthode de vérification utilisée jusqu'ici (relecture + scripts de contrôle) par cohérence avec le reste du projet.

---

## 7. Ce que je recommande comme prochaine étape

Le plan est globalement bien pensé mais a été écrit sans relire le code final de RH-01 — deux points structurants (cardinalité du poste, doublon d'écran) auraient produit un vrai problème d'architecture s'ils étaient passés inaperçus jusqu'au codage. Avec les deux décisions prises ici (modèle GPEC, un seul éditeur de poste), le plan est en état d'être réécrit puis implémenté. Il reste une décision mineure à ta charge (§4, classification indicative ou contraignante) avant que je commence.

Dis-moi si tu veux que je réécrive le plan corrigé avant de commencer le code, ou que je parte directement sur l'implémentation avec ces décisions en tête.

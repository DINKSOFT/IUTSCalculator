# Analyse du plan d'implémentation — Module RH-04 « Formation »

Relecture à la lumière du code réel du projet (post RH-03, migration 31, `SchemaCible = 31`) et de tout ce qui vient d'être appris pendant l'audit et la remise en état de RH-03 — notamment la découverte, en creusant `SQLiteService.cs`, que `PRAGMA foreign_keys` n'est **jamais activé** dans cette application. Cette découverte change la portée de plusieurs points du plan RH-04, au-delà des seules corrections habituelles.

**Verdict global** : l'architecture est cohérente et bien articulée avec RH-02/RH-03 (réutilisation du référentiel `Competences`, passerelle vers `EntretienBesoinsFormation`). Aucun conflit structurel majeur. Mais un point fondamental — invisible à la simple lecture du schéma — remet en question la portée réelle de plusieurs clauses `ON DELETE` du plan, et deux incohérences concrètes avec le code RH-03 déjà livré doivent être corrigées avant tout développement.

---

## 0. Découverte fondamentale : `PRAGMA foreign_keys` n'est jamais activé

`SQLiteService.CreateConnection()` (lignes 69-74) ouvre chaque connexion sans jamais exécuter `PRAGMA foreign_keys = ON;`. Par défaut, SQLite désactive l'application des clés étrangères pour chaque connexion tant qu'elle n'est pas explicitement activée. Le seul `PRAGMA foreign_keys = OFF;` du fichier (ligne 2151) est délibérément scopé à la migration 2 (reconstruction de tables) et redevient sans objet après — mais rien ne l'active jamais ailleurs.

**Conséquence directe, vérifiée sur le code existant** : toutes les clauses `ON DELETE CASCADE` / `ON DELETE SET NULL` du schéma — celles de RH-02, celles de RH-03 (que j'ai moi-même écrites la semaine dernière en pensant qu'elles avaient un effet), et celles proposées par ce plan RH-04 — sont **purement documentaires**. SQLite ne les applique jamais. La preuve la plus parlante est déjà dans le code : `FichePosteService.SupprimerCompetenceAsync` (RH-02) ne compte pas sur la cascade déclarée ailleurs — elle supprime **manuellement** les lignes `PosteCompetences` avant de supprimer la `Competence`, dans une transaction explicite. C'est le bon réflexe, et c'est la preuve que l'auteur original de RH-02 avait déjà compris (ou vérifié) que SQLite ne fait rien seul ici.

Cela ne remet pas en cause les corrections déjà faites sur RH-03 (retirer le CASCADE sur `EmployeeId`, les gardes d'immutabilité côté service) — ces corrections restent justes, et sont même *la seule chose qui protège réellement les données*, puisque le schéma seul ne protège rien. Mais ça change la lecture de ce plan RH-04 :

- Le point 2 du plan (« Pas de `ON DELETE CASCADE` sur `EmployeeId` dans `SessionInscriptions` ») est une bonne intention, mais elle est déjà satisfaite *de fait* par l'absence d'application des FK — ce n'est pas la clause SQL qui protège l'historique, c'est l'absence de vraie cascade nulle part. Il faut donc être précis dans le code : ne jamais écrire `DELETE FROM Employees` en supposant qu'une cascade ailleurs va « nettoyer » quoi que ce soit — elle ne le fera pas.
- À l'inverse, la `ON DELETE CASCADE` déclarée sur `PlanFormationId -> SessionsFormation` et sur `SessionId -> SessionInscriptions` **ne fonctionnera pas non plus**. Si `FormationService.SupprimerPlanFormationAsync` est implémenté comme un simple `DELETE FROM PlansFormation WHERE Id = @Id`, les `SessionsFormation` et `SessionInscriptions` associées ne seront **pas** supprimées — elles deviendront des lignes orphelines avec une `PlanFormationId`/`SessionId` pointant vers une ligne qui n'existe plus, silencieusement, sans la moindre erreur.

**Correction à intégrer** : `FormationService` doit implémenter tout nettoyage/cascade **explicitement en C#**, exactement comme `SupprimerCompetenceAsync` le fait déjà :
- `SupprimerPlanFormationAsync` doit supprimer lui-même les `SessionsFormation` et `SessionInscriptions` du plan dans une transaction (ou, mieux, refuser la suppression si des attestations ont déjà été délivrées — voir §1.1).
- `SupprimerSessionFormationAsync` doit supprimer lui-même ses `SessionInscriptions`.
- Les clauses `ON DELETE` dans le SQL du plan peuvent rester (elles documentent l'intention et ne coûtent rien), mais elles ne remplacent aucun code applicatif.

---

## 1. Corrections

### 1.1 Interdire la suppression d'un plan/d'une session contenant des attestations déjà délivrées

Directement dans la continuité du §0 et du principe déjà établi pour RH-03 (`SupprimerCampagneAsync` bloque la suppression d'une campagne contenant des entretiens `Validé`/`Clôturé`, des documents signés) : une attestation de formation délivrée (`SessionInscription.AttestationObtenue = true`) a la même valeur probatoire — c'est un document qui peut avoir été remis au salarié et servir de justificatif fiscal auprès du FAFPA/DGI. `SupprimerPlanFormationAsync`/`SupprimerSessionFormationAsync` doivent refuser la suppression si au moins une inscription de la session a `AttestationObtenue = true`, avec un message clair invitant à archiver/clôturer plutôt que supprimer. Sans cette garde, et sans cascade FK réelle (§0), une suppression malheureuse détruit un historique probatoire sans même l'avertissement qu'une vraie cascade aurait au moins rendu visible dans l'UI.

### 1.2 `ALTER TABLE ... ADD COLUMN` doit passer par le helper existant, pas une instruction brute

Le plan écrit littéralement :
```sql
ALTER TABLE EntretienBesoinsFormation ADD COLUMN SessionFormationId INTEGER REFERENCES SessionsFormation(Id) ON DELETE SET NULL;
```
Le projet a déjà un helper idempotent pour exactement ce cas : `AjouterColonneSiAbsente(connection, nomTable, nomColonne, type)` (`SQLiteService.cs`, ~ligne 2321), qui vérifie via `PRAGMA table_info` que la colonne n'existe pas déjà avant d'exécuter l'ALTER. Une instruction ALTER brute échoue avec « duplicate column name » si jamais elle s'exécute une seconde fois (script de réparation, migration relancée après un incident) — et casse alors toute la transaction de migration 32. À corriger : `AjouterColonneSiAbsente(connection, "EntretienBesoinsFormation", "SessionFormationId", "INTEGER REFERENCES SessionsFormation(Id)")`.

### 1.3 `StatutBesoinFormation.Valide` n'existe pas

Le plan dit que la passerelle RH-03→RH-04 « met à jour le statut du besoin vers `Retenu` ou `Valide` ». J'ai vérifié : l'énumération `StatutBesoinFormation` (créée en RH-03, migration 31, dans `Models/Evaluation.cs`) ne contient que 4 valeurs : `Demande` (0), `Retenu` (1), `Reporte` (2), `Rejete` (3) — il n'y a pas de `Valide`. Le plan présuppose une valeur qui n'a jamais été créée. Deux options, à trancher avant de coder :
- soit c'est une erreur de rédaction et seul `Retenu` doit être posé (le plus probable — c'est déjà l'état « accepté » du workflow existant, et le cycle de vie réel de la formation se pilote via `StatutSessionFormation`, pas via ce champ) ;
- soit une vraie distinction « pris en charge mais pas encore réalisé » vs « réalisé et confirmé » est voulue, et il faut alors ajouter une valeur à l'énumération existante — ce qui touche un fichier déjà livré et testé (`EvaluationTests.cs`).

### 1.4 Agrégats TPA : reprendre le filtre `IsAnnual = 0 AND IsRegularization = 0`

Le plan propose de calculer la masse salariale soumise à la TPA par un `SUM(TaxCalculations.AssietteTPA)` sur l'exercice. Or `TaxCalculations` contient, pour un même salarié et une même période, plusieurs *catégories* de lignes distinguées par deux colonnes booléennes : un calcul mensuel normal, mais aussi des lignes de récapitulatif annuel (`IsAnnual`) et des lignes de régularisation (`IsRegularization`). `EtatAnnuelSalairesService.RemplirLignesAsync` (module DADS, déjà en production) filtre explicitement `WHERE c.CalculationDate BETWEEN ... AND c.IsAnnual = 0 AND c.IsRegularization = 0` pour ne compter chaque mois qu'une fois. Un `SUM` sans ce filtre gonflera la masse salariale TPA et donc tous les ratios qui en découlent (effort de formation, taux de couverture). Autre simplification possible : la « TPA théorique due » n'a pas besoin d'être recalculée via un taux — `TaxCalculations.TaxePatronale` est déjà la valeur calculée (`AssietteTPA × ParametresSociaux.TauxTaxePatronale`, `TaxCalculatorService.cs` ligne 435) ; il suffit de la sommer avec le même filtre, pas de passer par un hypothétique service de barème dédié à la TPA (qui n'existe pas sous cette forme — le taux vit dans `ParametresSociaux`, pas dans `BaremeService`).

---

## 2. Ajouts recommandés

### 2.1 Interdire le hard-delete d'un `OrganismeFormation`/`FormationCatalogue` référencé par une session réelle

Le plan prévoit à juste titre un champ `Actif` sur les deux — c'est le bon réflexe (désactivation plutôt que suppression physique), cohérent avec l'absence de vraie cascade FK (§0). Mais il faut le rendre explicite au niveau service : `SupprimerOrganismeAsync`/`SupprimerFormationCatalogueAsync` ne doivent permettre qu'un `Actif = 0` dès qu'au moins une `SessionFormation` référence la ligne — jamais un `DELETE` physique. Le nom de l'organisme agréé et l'intitulé de la formation doivent rester lisibles sur une attestation déjà délivrée, même après que l'organisme a cessé son activité ou que la formation a été retirée du catalogue courant.

### 2.2 Résoudre les compétences des données d'amorçage par `Code`, jamais par ID littéral

Le plan prévoit « 3 formations de référence liées aux compétences existantes ». Le seul précédent de données d'amorçage liées à une compétence dans ce projet (`InsererCompetenceInitiale`, avec `INSERT OR IGNORE INTO Competences (Code, ...)`) repose sur `Code` comme clé stable, jamais sur l'`Id` auto-incrémenté (qui n'est garanti stable ni entre versions de la migration, ni entre dossiers). Le seed de `FormationsCatalogue` doit résoudre `CompetenceViseeId` via une sous-requête, par exemple `(SELECT Id FROM Competences WHERE Code = 'MANAGEMENT')`, jamais un entier codé en dur.

### 2.3 Vérifier les clés de ressources XAML avant d'écrire `FormationView.xaml` — leçon très fraîche

Les trois derniers plantages au lancement de `EntretiensEvaluationsView.xaml` (`IconPlus`, `IconTrash`, puis `InkPrimaryBrush`) avaient tous la même cause : une ressource nommée de façon « logique » mais jamais définie dans `App.xaml`. Avant d'écrire le premier `Tag="{StaticResource IconXxx}"` ou `Foreground="{StaticResource XxxBrush}"` dans `FormationView.xaml`, il faut vérifier contre la liste réelle des clés de `App.xaml` (`IconAdd`, `IconDelete`, `IconEdit`, `IconLock`, `IconBack`, `IconExport`, `InkBrush`, `InkMutedBrush`, `InkSubtleBrush`, etc.) — et n'ajouter une clé nouvelle à `App.xaml` que si le concept manque réellement, jamais en supposant qu'un nom plausible existe déjà.

### 2.4 Rappel de câblage (double inscription) — déjà bien anticipé par le plan

Le plan mentionne correctement l'intégration de `Migration32_FormationEtTPA` dans `EnsureSchema` (nom de méthode vérifié exact) et `CreerSchemaComplet`, ainsi que les trois éléments de navigation. Rien à corriger ici — juste à confirmer que les trois pièces de navigation (bouton avec `IsNavFormationVisible` dédiée, `DataTemplate`, entrée `_navMenuItems`) utilisent bien la même clé littérale, comme pour RH-03.

### 2.5 Vérifier la dérive de `ETAT-DEVELOPPEMENT.md` avant tout commit

Rappel opérationnel : ce fichier a déjà été écrasé une fois par Visual Studio resté ouvert pendant que je le mettais à jour. Avant tout commit qui le touche pour RH-04, vérifier son mtime/sa taille sur le poste au lieu de l'écraser à l'aveugle.

---

## 3. Ce qui est déjà solide, sans changement nécessaire

- Architecture globale cohérente (le diagramme Mermaid reflète correctement les dépendances Modèle → Service → ViewModel → View).
- Réutilisation de `Competences` (RH-02) et `EntretienBesoinsFormation` (RH-03) plutôt que duplication de référentiel.
- `BudgetPrevisionnel` en centimes (`INTEGER`) — cohérent avec la convention monétaire déjà en place partout dans l'app.
- Sauvegarde automatique `.avant-v32.bak` avant migration — cohérent avec `SauvegarderAvantMigration`, déjà utilisé pour les migrations précédentes.
- `ScrollViewer` global + `MinHeight` anticipés dès la conception de `FormationView.xaml` — la leçon RH-01/02/03 est bien intégrée.
- Contrainte unique anti-doublon d'inscription prévue dès la conception (`UX_SessionInscriptions_Session_Employe`) — même réflexe que l'index unique ajouté sur RH-03.
- Conventions d'export PDF/Excel identiques à RH-02/RH-03 (4 feuilles, bloc de signatures tripartite) — pas de nouvelle mécanique à inventer.

---

## 4. Prochaine étape

Le point le plus important de cette analyse (§0) n'est pas spécifique à RH-04 — c'est une découverte structurelle sur toute la base qui rend plusieurs clauses `ON DELETE` déjà écrites (RH-02, RH-03) décoratives, sans que cela n'ait jamais causé de problème visible jusqu'ici parce que le code service a, jusqu'à présent, toujours implémenté les gardes nécessaires à la main. RH-04 doit continuer cette discipline, pas s'appuyer sur le schéma seul.

Dis-moi si tu veux que je commence le développement de RH-04 une fois que le code sera livré, ou si tu préfères d'abord relire ce plan corrigé.

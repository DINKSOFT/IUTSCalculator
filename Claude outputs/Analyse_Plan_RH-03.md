# Analyse du plan d'implémentation — Module RH-03 « Entretiens & Évaluations »

Relecture du plan à la lumière du code réel du projet (post RH-02, migration 30, `SchemaCible = 30`), dans le même esprit que l'analyse faite avant RH-02 : valider ce qui est solide, corriger ce qui entre en contradiction avec le code ou les conventions établies, et ajouter ce qui manque avant que le développement ne commence.

**Verdict global** : le plan est bien construit et s'appuie correctement sur RH-01 et RH-02 (réutilisation de `Employees.ManagerMatricule`, `Poste.SuperieurPosteId`, du catalogue `Competences`). Aucun conflit structurel du type de ceux trouvés avant RH-02 (pas de doublon d'écran, pas de conflit de cardinalité). Le point le plus agréable à constater : la section XAML reprend déjà, comme règle générale, la leçon du correctif de disposition du jour même sur `SuiviPostesBudgetView.xaml` (`ScrollViewer` global + `MinHeight`) — bon signe que les correctifs de ce projet s'accumulent au lieu de se répéter. Sept points demandent correction ou ajout avant le développement, tous fondés sur des conventions déjà en place dans le code, pas sur des choix arbitraires de ma part.

---

## 1. Corrections

### 1.1 `NoteGlobale` : figer la note à la validation, pas seulement la stocker

Le plan stocke `EntretiensEvaluation.NoteGlobale` comme une moyenne pondérée des critères, mais ne précise jamais QUAND elle est recalculée. C'est exactement la famille de bug qui a coûté le plus cher à ce projet (items 18 à 21 de `ETAT-DEVELOPPEMENT.md` : des champs dénormalisés/mis en cache qui ne se rafraîchissaient pas quand la source changeait — fiches employé, historique de calcul, listes de salariés). Si un évaluateur modifie une note de critère après avoir déjà enregistré l'entretien, `NoteGlobale` doit être recalculée immédiatement, sinon elle affiche une valeur périmée dans le tableau de bord et l'export Excel.

**Règle à ajouter explicitement** : `NoteGlobale` (et, par cohérence, le taux d'atteinte moyen des objectifs `N-1` affiché en synthèse) est recalculée à chaque modification de `EntretienCriteres`/`EntretienObjectifs` **tant que `Statut < Validé`**. À partir de `Validé` (et a fortiori `Clôturé`), l'entretien devient un document figé : toute tentative de modifier un critère, un objectif ou un besoin de formation doit être bloquée côté service, pas seulement côté UI — même logique d'immutabilité que les périodes de paie closes (`PeriodePaie`) ou les rappels sur période close. C'est une garantie légale autant que technique : une fiche d'entretien signée ne doit plus bouger.

### 1.2 Retirer `ON DELETE CASCADE` sur `EntretiensEvaluation.EmployeeId`

Vérifié dans `SQLiteService.cs` : sur les 15 clauses `FOREIGN KEY(EmployeeId) REFERENCES Employees(EmployeeId)` existantes du schéma, une seule utilise `ON DELETE CASCADE` (`PersonnesACharge` — une donnée de profil, pas un historique). Toutes les autres — bulletins, absences, prêts, rappels, saisies-arrêts — n'ont **aucune** clause `ON DELETE`, précisément pour que l'historique du salarié survive même si sa fiche est un jour supprimée (`DataService.cs` contient bien un `DELETE FROM Employees WHERE EmployeeId = @EmployeeId`, donc ce n'est pas un cas théorique). Un entretien d'évaluation est un historique, pas un attribut de profil : il doit suivre la même règle que les bulletins et les rappels, pas celle des personnes à charge. Retirer `ON DELETE CASCADE` de `EntretiensEvaluation.EmployeeId` (garder `ON DELETE SET NULL` pour `EvaluateurMatricule`, qui est correct — un évaluateur qui part ne doit pas faire disparaître l'entretien du salarié évalué).

### 1.3 Utiliser `Employee.EstActif`, pas `StatutSalarie = 0` seul, pour l'initialisation de campagne

`Employee.EstActif => StatutSalarie == 0 && (!DateSortie.HasValue || DateSortie.Value.Date >= DateTime.Today)` (vérifié dans `Employee.cs`). Le plan propose de cibler l'effectif avec `StatutSalarie = 0` seul lors de l'initialisation automatique d'une campagne — ça ignore un salarié dont le départ est déjà enregistré (`DateSortie` dans le passé) mais dont le drapeau `StatutSalarie` n'a pas encore été mis à jour. C'est exactement la même distinction qui avait motivé le filtrage par `EstActif` sur les écrans de lot (dette n°20). À appliquer ici aussi.

### 1.4 Ajouter un index unique `(CampagneId, EmployeeId)` sur `EntretiensEvaluation`

Le plan prévoit une « initialisation automatique des entretiens pour l'effectif actif » mais ne protège nulle part contre un double lancement de cette initialisation (bouton recliqué, campagne rouverte). Sans contrainte, on se retrouve avec deux lignes d'entretien pour le même salarié dans la même campagne. Ajouter :
```sql
CREATE UNIQUE INDEX IF NOT EXISTS UX_EntretiensEvaluation_Campagne_Employe
    ON EntretiensEvaluation(CampagneId, EmployeeId);
```
et faire en sorte que le service d'initialisation utilise une logique d'insertion idempotente (vérifie l'existence avant d'insérer, ou `INSERT OR IGNORE` suivi d'un rapport des lignes déjà présentes) plutôt que de laisser l'index unique lever une exception SQL brute.

---

## 2. Ajouts recommandés

### 2.1 Résolution de l'évaluateur N+1 : préciser l'ordre de priorité et le cas d'échec

Le plan dit « en s'appuyant sur `Employees.ManagerMatricule` **ou** sur le supérieur direct du poste ». Ce « ou » doit devenir un ordre explicite, sinon deux salariés dans la même situation pourraient se voir attribuer des évaluateurs différents selon l'implémentation du jour :

1. **`Employees.ManagerMatricule`** si renseigné (affectation explicite, la plus fiable).
2. Sinon, dérivation depuis l'organigramme : `Employee.PosteId` → `Poste.SuperieurPosteId` → salariés dont `PosteId = SuperieurPosteId` (actifs). Ici, `OrganigrammeService.ChargerTitulairesPosteAsync(posteId)` fait déjà exactement cette recherche (vérifié dans `OrganigrammeService.cs`, utilisé par RH-02 pour lister les titulaires d'un poste) — RH-03 doit le réutiliser plutôt que de réécrire la requête.
3. **Si cette recherche renvoie zéro ou plusieurs candidats** (poste vacant, poste GPEC à plusieurs titulaires, pas de `SuperieurPosteId` défini) : ne jamais deviner. Laisser `EvaluateurMatricule` vide et faire remonter ce salarié dans une liste « évaluateur à assigner manuellement » visible dans l'onglet Campagne — même philosophie que l'import CSV, qui crée un nouveau salarié plutôt que de deviner un rapprochement sur homonymie douteuse.

**Point pratique à anticiper** : comme vu ensemble aujourd'hui sur le tableau de bord GPEC (écran encore à 0 partout), les rattachements poste/salarié de l'organigramme ne sont probablement pas encore tous renseignés. La première campagne RH-03 risque donc de générer une liste « évaluateur à assigner » non négligeable — ce n'est pas un bug, c'est attendu, mais l'écran doit le montrer clairement plutôt que de laisser des lignes avec un évaluateur silencieusement vide.

### 2.2 Lier `EntretienBesoinsFormation` au catalogue de compétences quand c'est possible

`EntretienCriteres.CompetenceId` référence proprement `Competences.Id` (FK nullable), mais `EntretienBesoinsFormation.CompetenceVisee` reste un simple `TEXT` libre — alors que ce champ est justement celui présenté comme la passerelle vers RH-04. Ajouter une colonne `CompetenceId INTEGER` (FK nullable vers `Competences(Id) ON DELETE SET NULL`) à côté de `ThemeFormation` (qui reste libre pour les besoins hors catalogue) : ça permet à RH-04, plus tard, d'agréger les besoins de formation par compétence catalographiée plutôt que de reparser du texte libre.

### 2.3 Catégorie des critères : dériver de `Competences.Domaine` quand le critère est catalographié

`EntretienCriteres.Categorie` est un `TEXT` libre, alors que RH-02 a déjà un `DomaineCompetence` (Technique, Management, Comportementale, Réglementaire). Pas besoin de changer le schéma, mais la convention côté service doit être : quand `CompetenceId` est renseigné, `Categorie` est alimentée depuis `Competences.Domaine`/`DomaineLibelle` (cohérence avec le vocabulaire déjà utilisé dans le référentiel des compétences) ; le texte libre ne sert que pour les critères hors catalogue (ex. « Organisation & Rigueur » générique). Sinon on se retrouve avec deux vocabulaires de catégorisation qui dérivent l'un de l'autre au fil du temps.

### 2.4 Rappel de câblage (double inscription, déjà source d'incidents sur ce projet)

Deux points où ce projet s'est déjà fait piéger, à respecter scrupuleusement pour RH-03 :
- **Migration 31** : elle doit être appelée à la fois dans la cascade `if (version < 31) { Migration31_...(connection); EnregistrerVersion(connection, 31); }` **et** dans `CreerSchemaComplet` (comme migrations 29/30), sinon un dossier neuf naît sans les tables RH-03.
- **Navigation** : la barre latérale de `MainWindow.xaml` n'est pas générée depuis la liste de recherche (`_navMenuItems`) — chaque nouvel écran a besoin des **trois** éléments séparément : le `<Button>` avec sa `Visibility` liée à une propriété `IsNavEntretiensVisible` dédiée, le `DataTemplate`, et l'entrée `_navMenuItems.Add(...)` avec la même clé littérale passée à `IsItemVisible(...)`. Oublier l'un des trois est l'erreur la plus fréquente de ce projet sur les nouveaux écrans (déjà rencontrée avant DEC-05/PIL-01).

---

## 3. Ce qui est déjà solide, sans changement nécessaire

- Modèle de campagne, statuts, workflow en 5 étapes (Planifié → Clôturé) : cohérent avec le reste de l'app.
- Réutilisation de `PosteCompetences` (RH-02) pour initialiser les critères types d'un poste : bonne pratique, évite de dupliquer un référentiel.
- Stockage `NoteGlobale` en `TEXT` culture-invariante : cohérent avec le traitement des taux (`TauxSalarial`, etc.) ailleurs dans le schéma.
- `AnneeExercice` en colonne explicite plutôt que dérivée de `DateDebut` : cohérent avec le principe déjà en place pour les regroupements annuels/mensuels (ex. `TaxCalculations`).
- Export PDF tripartite A4 et Excel 4 feuilles : conventions identiques à RH-02, pas de nouvelle mécanique à inventer.
- Section XAML anticipant déjà `ScrollViewer` global + `MinHeight` : bon report du correctif du jour.

---

## 4. Prochaine étape

Aucun de ces points n'est un choix arbitraire de ma part — tous s'appuient sur du code ou de la documentation déjà présents dans ce projet (`ETAT-DEVELOPPEMENT.md`, `SQLiteService.cs`, `Employee.cs`, `OrganigrammeService.cs`). Le plan corrigé (schéma avec l'index unique et sans le `CASCADE`, règle de gel de `NoteGlobale`, algorithme de résolution d'évaluateur à 3 étapes) peut être transmis tel quel à l'implémentation. Dis-moi si tu veux que je commence le développement, ou si — comme pour RH-02 — tu préfères d'abord relire.

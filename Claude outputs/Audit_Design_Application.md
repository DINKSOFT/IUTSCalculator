# Audit du design — cohérence visuelle & ergonomie de l'application

Revue systématique des 40 vues et fenêtres WPF de « Sigma Paie & RH », comparées au système de design défini dans `App.xaml` (palette sémantique Ink/Ground/Surface/Rail/Accent, styles `Form*Style`, `MenuButtonStyle`/`SecondaryButtonStyle`/`DangerButtonStyle`, `CardBorderStyle`, `FormDataGridStyle`). Revue en 4 lots parallèles, ~40 fichiers, aucune modification appliquée à ce stade.

---

## Résumé exécutif

Le système de design lui-même est solide et bien pensé (palette sémantique claire, philosophie documentée : « l'accent ne sert qu'à trois choses »). L'adhésion à ce système est **très inégale selon l'ancienneté et l'auteur de l'écran** :

- **Noyau paie/calcul** (Bulletin, Calcul, Historique, Paramètres, etc.) : excellente adhésion, quasi exemplaire.
- **Déclarations & pilotage** (CNSS, IUTS/TPA, Livre de paie, Tableau de bord…) : excellente adhésion, un seul motif d'incohérence répété (badge « succès » en 3 teintes différentes codées en dur).
- **Données RH & paramétrage** (Rubrique, Absence, Prêt, Période de paie…) : bon niveau global, mais deux fichiers (`ConventionCollectiveView`, `SoldeToutCompteView`) abandonnent largement le système.
- **Modules RH les plus récents** (Organigramme, Suivi budgétaire, Entretiens, **Formation**, **Recrutement**) et **fenêtres d'authentification** (Login, Réinitialisation, Sélection de dossier) : c'est ici que la cohérence se dégrade le plus nettement. `FormationView` et `RecrutementView` réinventent quasiment tous leurs contrôles en dur ; les fenêtres de connexion n'utilisent presque aucune ressource sémantique et affichent même une couleur de marque différente (`#0078D7` bleu Windows au lieu du teal `#14675A` de l'app) et un titre de fenêtre resté « IUTS Calculator » au lieu de « Sigma Paie & RH ».

**Deux fichiers semblent être du code mort** : `PayslipMonthlyView.xaml` et `BulletinPaieView.xaml` ne sont référencés nulle part dans `MainViewModel`/`MainWindow` et n'ont manifestement jamais reçu le traitement du système de design (le second est un mock 100% statique sans le moindre binding).

---

## Motifs transversaux (le plus grand levier — corriger une fois, effet sur plusieurs fichiers)

| # | Motif | Fichiers touchés |
|---|---|---|
| 1 | **Code mort non câblé**, jamais mis à jour vers le système actuel | `PayslipMonthlyView.xaml`, `BulletinPaieView.xaml` |
| 2 | **Badge « succès/clôturé »** codé en dur avec 3 teintes de vert différentes au lieu d'une ressource `SuccessTint`/`SuccessRule` | `EcriturePaieView` (#EBF7F0/#2BA24C), `OrdreVirementView` (idem), `AuditView` (#E6F0ED/#2BA24C) |
| 3 | **Fond de barre de recherche/filtre** `#F8FAFC` codé en dur au lieu de `SurfaceAltBrush` | `ProfilPaieView`, `VariablePaieView`, `AbsenceView`, `PretView`, `SaisieOppositionView`, `OrganigrammeView` |
| 4 | **Bouton « Supprimer »** tantôt `DangerButtonStyle`, tantôt `SecondaryButtonStyle` + `Foreground` forcé — parfois les deux dans le même fichier | `ConventionCollectiveView`, `SoldeToutCompteView`, `EmployeeManagementView`, `RecrutementView` |
| 5 | **Plusieurs boutons « primaires » (accent plein) visibles en même temps** — viole la règle « un seul par écran » | `BaremeView`, `ProfilPaieView`, `RappelPaieView` (×3 onglets), `AvantageNatureView`, `OrganigrammeView`, `EntretiensEvaluationsView` (×2), `FormationView` (×2), `DossierSelectionWindow` (le pire cas : Nouveau/Modifier/**Supprimer** tous en accent plein, + Enregistrer/Annuler tous deux en accent plein) |
| 6 | **Alias historiques** (`PrimaryBlueBrush`, `PrimaryBlueDarkBrush`, `PrimaryBlueDarkerBrush`, `GrayLightBrush`, `GrayMediumBrush`, `GrayDarkBrush`) utilisés dans du code récent au lieu des noms sémantiques | `AvantageNatureView`, `SoldeToutCompteView`, `OrganigrammeView`, `RecrutementView`, `DossierSelectionWindow` |
| 7 | **`DatePicker` sans `FormDatePickerStyle`** | `AuditView`, `SaisieOppositionView`, `RappelPaieView` (×4), `AvantageNatureView` (×2) |
| 8 | **`TextBox`/`ComboBox`/`CheckBox` sans style `Form*Style`** (retombent sur le rendu WPF par défaut) | `HeuresSupplementairesView`, `ConventionCollectiveView` (quasi tout le fichier), `SoldeToutCompteView` (tout le panneau gauche), `EntretiensEvaluationsView` (la plupart des champs), `FormationView` (systémique), `RecrutementView` (systémique) |
| 9 | **`DataGrid` sans `FormDataGridStyle`/`MainDataGridStyle`** (re-stylé à la main) | `SoldeToutCompteView`, `EntretiensEvaluationsView`, `FormationView`, `RecrutementView` |
| 10 | **Émojis bruts** (🔒 ⚠️ ✔) au lieu des glyphes `Segoe MDL2 Assets` (`IconXxx`) déjà en place partout ailleurs | `HeuresSupplementairesView`, `ConventionCollectiveView`, `EntretiensEvaluationsView`, `EmployeeManagementView` |
| 11 | **Styles locaux dupliqués et divergents** (`StatCardStyle`/`TabHeaderBtnStyle`/`KpiCardStyle`/`IconButtonStyle`) copiés-collés d'un écran RH à l'autre avec des valeurs légèrement différentes au lieu d'être promus dans `App.xaml` | `OrganigrammeView`, `SuiviPostesBudgetView`, `EntretiensEvaluationsView`, `FormationView`, `RecrutementView` |
| 12 | **Deux idiomes d'onglets concurrents** dans la même famille de modules RH : barre d'onglets plate à base de `RadioButton` (Organigramme/Suivi budgétaire/Entretiens) vs `TabControl` natif WPF (Formation/Recrutement) — se voit immédiatement en changeant d'écran | `FormationView`, `RecrutementView` vs les 3 autres |
| 13 | **Fenêtres d'authentification hors système** : couleurs codées en dur de bout en bout, couleur de marque différente (`#0078D7` au lieu du teal `#14675A`), titre de fenêtre resté « IUTS Calculator » | `LoginWindow`, `PasswordResetWindow`, `DossierSelectionWindow` |
| 14 | **États de chargement incohérents** : voile plein écran (`SettingsView`), barre de progression en pied de page (`CalculationView`, `BulletinBatchView`, `RappelPaieView`), ou rien du tout (`BatchSalaryEstimationView`) | Transversal |
| 15 | **États vides manquants** sur certaines grilles avant premier chargement | `BatchSalaryCalculationView`, `BatchSalaryEstimationView`, `HistoryView` (filtre → 0 résultat) |

---

## Détail par fichier

### Lot A — Écrans cœur de paie (PayslipMonthlyView, BulletinPaieView, BulletinView, BulletinBatchView, CalculationView, BatchSalaryCalculationView, BaseSalaryEstimatorView, BatchSalaryEstimationView, HistoryView, SettingsView)

**`PayslipMonthlyView.xaml`** — Non câblé dans `MainViewModel` (code mort probable). Titre en `FontSize="20" FontWeight="Bold"` au lieu de `ScreenTitleStyle`. Aucun `TextBlock` stylé (texte noir par défaut). Aucun `CardBorderStyle` — contenu posé directement sur le fond. Boutons « Imprimer »/« Exporter PDF » sans style. Aucun état vide/chargement.

**`BulletinPaieView.xaml`** — `x:Class="BulletinPaie.Views.BulletinPaieView"` : espace de noms qui ne correspond même pas au projet (`IUTSCalculator.Views`), non référencé ailleurs. Définit son propre jeu de styles locaux (`HeaderTextStyle`, `TitleStyle`, `LabelStyle`…) totalement déconnecté du système. `BorderBrush="Black"` codé en dur des dizaines de fois. En-têtes de tableau `Background="LightGray"` (bande de couleur pleine — exactement ce que le système évite). **Zéro binding de données dans tout le fichier** — c'est une maquette statique inachevée ("Octobre 2024", "217854Y" etc. en dur). À supprimer ou à terminer.

**`BulletinView.xaml`** — Très bonne adhésion dans le panneau « paramètres » (`ScreenTitleStyle`, `CardBorderStyle`, tous les `Form*Style`, un seul bouton primaire). Le bloc « APERÇU » reproduit volontairement un document légal figé (commentaire l.208-211 l'explique), ce qui justifie en partie mais n'excuse pas entièrement : hex codés en dur `#CCCCCC`/`#FAFAFA`/`#AAAAAA`/`#555555` (en-tête société), `#EFEFEF` ×8 (cellules identité), deux teintes différentes pour des en-têtes de section conceptuellement identiques (`#4F6228` olive vs `#7F5F28` brun), `Foreground="#A33"` pour une déduction négative (alors que `DangerBrush` existe exactement pour ça), `Foreground="Gray"` nommé au lieu de `InkSubtleBrush`. Bon état vide (l.502-514).

**`BulletinBatchView.xaml`** — Très cohérent, aucun problème notable. Hauteur de bouton `28px` (deux boutons) au lieu du standard ~30px, mineur. Pas de confirmation avant génération PDF en masse, faible sévérité.

**`CalculationView.xaml`** — Exemplaire. Un seul écart mineur de motif (style `Label` vs `TextBlock` pour les libellés de champ, contrairement à d'autres écrans).

**`BatchSalaryCalculationView.xaml`** — Cohérent. Manque un état vide dans la grille de résultats avant chargement.

**`BaseSalaryEstimatorView.xaml`** — Aucun problème. Bon usage de `DataTrigger` pour masquer le panneau de résultat tant qu'il n'y a rien à montrer.

**`BatchSalaryEstimationView.xaml`** — Cohérent côté style. Aucun état vide avant import CSV ; aucune barre de progression pendant l'import/le calcul en masse (contrairement à `BulletinBatchView`).

**`HistoryView.xaml`** — Très cohérent, bon usage de `DangerButtonStyle` pour « Supprimer ». Pas de confirmation visible dans le XAML pour la suppression (peut être gérée côté ViewModel — à vérifier). Pas de message « aucun résultat » quand un filtre ne retourne rien.

**`SettingsView.xaml`** — Globalement bon (un seul bouton primaire, `DangerButtonStyle` correct pour 2 actions destructrices). `Background="#99FFFFFF"` codé en dur pour le voile de chargement. `TabControl` sans style explicite — risque de chrome bleu système par défaut si `App.xaml` ne définit pas de style de secours (à vérifier). « Réinitialiser » (remise à zéro complète des paramètres) en `SecondaryButtonStyle` au lieu de `DangerButtonStyle`, sans confirmation visible — incohérent avec le reste du même fichier qui traite bien les actions destructrices ailleurs.

### Lot B — Déclarations & pilotage (DeclarationCnssView, DeclarationIutsTpaView, LivrePaieView, EcriturePaieView, OrdreVirementView, EtatAnnuelSalairesView, TableauBordMasseSalarialeView, AuditView, ControleEquivalenceView, BaremeView)

Lot le plus discipliné dans l'ensemble. `DeclarationCnssView`, `DeclarationIutsTpaView`, `EtatAnnuelSalairesView`, `TableauBordMasseSalarialeView`, `ControleEquivalenceView` : **aucun problème notable** (le dernier est même le fichier le plus propre de tout l'audit, bon usage de `AccentTintBrush`/`WarningTintBrush` pour ses bandeaux de verdict).

**`LivrePaieView.xaml`** — Grille à 19 colonnes de largeur fixe, défilement horizontal nécessaire sur écran standard ; pas un bug, mais une vraie contrainte ergonomique pour un registre consulté fréquemment.

**`EcriturePaieView.xaml`** — Badge « ✓ PIÈCE ÉQUILIBRÉE » en hex codé en dur (`#EBF7F0`/`#2BA24C`/`#1F7A4D`) au lieu de `SuccessBrush`. Couleur également codée dans un `ConverterParameter` (`'text:#1F7A4D;#9C2F28'`) — contourne complètement le système de ressources.

**`OrdreVirementView.xaml`** — Même anti-motif : hex en dur dans les `ConverterParameter`, badge « ✓ PÉRIODE CLÔTURÉE » codé en dur.

**`AuditView.xaml`** — Même badge vert codé en dur (3ᵉ teinte différente). Les 2 `DatePicker` de filtre (Date début/fin) n'ont **aucun style**, contrairement à ceux de `BaremeView` juste à côté dans le même lot — écart visible dans la même ligne de filtres.

**`BaremeView.xaml`** — Deux boutons primaires (accent plein) visibles simultanément une fois un barème sélectionné (« Nouveau barème » + « Enregistrer »). Colonnes éditables « De/À (FCFA) », « Taux (%) », « Réduction IUTS » : en-tête aligné à droite mais **cellules non stylées** en `MoneyTextStyle` — seul l'en-tête est aligné, pas la valeur. Suppression sans confirmation visible dans le XAML.

### Lot C — Données RH & paramétrage (RubriqueView, ProfilPaieView, VariablePaieView, HeuresSupplementairesView, ConventionCollectiveView, AbsenceView, PretView, SaisieOppositionView, RappelPaieView, AvantageNatureView, SoldeToutCompteView, EmployeeManagementView, PeriodePaieView)

**`RubriqueView.xaml`**, **`PeriodePaieView.xaml`**, **`EmployeeManagementView.xaml`**, **`AbsenceView.xaml`**, **`PretView.xaml`** — Très bon niveau, quasi référence (surtout `EmployeeManagementView` qui dérive proprement ses propres styles `FieldBox`/`FieldCombo`/`FieldDate` de `Form*Style`).

**`ProfilPaieView.xaml`** — Jusqu'à **4 boutons accent plein visibles en même temps** (Nouveau profil, 2× Enregistrer sur des panneaux différents, Rattacher) ; deux boutons distincts portent le même libellé « Enregistrer » pour des actions différentes — ambigu pour l'utilisateur.

**`VariablePaieView.xaml`** — Un seul écart (`#F8FAFC` en dur), sinon propre, bien commenté.

**`HeuresSupplementairesView.xaml`** — Sélecteurs Mois/Année et champ de filtre sans style. Bandeau « période clôturée » en hex codé en dur avec un cadenas emoji. Deux occurrences de `#14675A` codé en dur — qui reproduit **littéralement** la valeur de `AccentBrush` au lieu d'y faire référence.

**`ConventionCollectiveView.xaml`** — **Le fichier le plus touché du lot** : quasiment tous les `TextBox`/`ComboBox`/`CheckBox` du fichier (grille principale + 2 onglets + simulateur) sont sans style. 3 boutons « Suppr. » en `SecondaryButtonStyle` + `Foreground` forcé au lieu de `DangerButtonStyle`. Bandeaux de conformité en hex codés en dur avec émojis (✔/⚠) bruts. `AccentBrush` utilisé comme couleur de texte décorative à répétition, bien au-delà des « 3 usages » prévus.

**`SaisieOppositionView.xaml`** — Bon niveau global (astérisques sur champs requis, `ScrollViewer` qui évite tout débordement, `DangerButtonStyle` correct pour Supprimer). Un badge en hex codé en dur juste à côté d'un badge correctement stylé — contraste visible. 2 `DatePicker` sans style.

**`RappelPaieView.xaml`** — 2 boutons primaires visibles simultanément, et ce **sur les 3 onglets** de l'écran. 4 `DatePicker`, 2 `RadioButton`, 3 `CheckBox` sans style. Point positif : seul écran du lot avec une vraie barre de progression liée à `IsBusy`.

**`AvantageNatureView.xaml`** — Utilise l'alias historique `PrimaryBlueBrush` au lieu de `AccentBrush2`. 2 boutons primaires visibles en même temps. 2 `DatePicker` et 5 `CheckBox` sans style.

**`SoldeToutCompteView.xaml`** — **Fichier le plus incohérent du lot.** Tout le panneau gauche « Salarié concerné » et le filtre d'historique n'utilisent **aucun** `Form*Style`. La grille « Historique des départs » est le seul `DataGrid` de tout le lot à ne pas utiliser `FormDataGridStyle`. Le détail du décompte de rupture est construit à la main avec ~14 `Border`+`Grid` empilés (250+ lignes) plutôt qu'un `DataGrid` — divergence structurelle importante, difficile à maintenir en cohérence avec le reste.

**`EmployeeManagementView.xaml`** — Malgré son excellent niveau général : un bandeau d'alerte codé en dur (`#FEF2F2`/`#EF4444`/`#B91C1C`) juste à côté d'un bandeau quasi identique correctement stylé en `WarningTintBrush` — incohérence *interne au même fichier*. Bouton « Supprimer » principal en `SecondaryButtonStyle` forcé alors que le bouton « Retirer » (personne à charge) juste en dessous utilise correctement `DangerButtonStyle`.

### Lot D — Modules RH les plus récents & fenêtres (Organigramme, SuiviPostesBudget, EntretiensEvaluations, Formation, Recrutement, Login, PasswordReset, DossierSelection)

**`OrganigrammeView.xaml`** — Le plus discipliné des 5 écrans RH récents. Quelques hex codés en dur isolés, alias `PrimaryBlueDarkBrush` non documenté utilisé à 6 reprises, une `CheckBox` sans style, 2 boutons primaires visibles en même temps (export + enregistrer d'onglet).

**`SuiviPostesBudgetView.xaml`** — Badge de statut GPEC (`DataGridTemplateColumn`) utilisant une palette entièrement codée en dur (10 valeurs hex), y compris un violet `#6A1B9A` qui n'a **aucun équivalent sémantique** dans le système — une couleur véritablement inventée pour l'occasion.

**`EntretiensEvaluationsView.xaml`** — Quasiment aucun champ de saisie autonome n'utilise `Form*Style` (recherche, filtres, champs de campagne, évaluateur/date…). Tous les `DataGrid` sont restylés à la main. Une modale plein écran avec voile noir (`#80000000`) pour l'édition de campagne — un motif d'interaction unique, alors qu'Organigramme/SuiviPostesBudget utilisent un panneau latéral inline pour la même tâche de création/édition. Boutons de suppression « ✕ » inline avec `Foreground="#C62828"` codé en dur, répété 4 fois.

**`FormationView.xaml`** — **Le fichier le plus déviant de tout l'audit avec RecrutementView.** Quasiment aucun bouton du fichier n'utilise `MenuButtonStyle`/`SecondaryButtonStyle`/`DangerButtonStyle` — tous réinventés en dur, sans état hover/pressed/disabled partagé. Titre d'écran en `TextBlock FontSize="16" FontWeight="Bold"` au lieu de `ScreenTitleStyle` (21px) — le titre de page paraît nettement plus faible que sur les écrans voisins. Utilise un `TabControl` natif WPF, un idiome de navigation différent de la barre d'onglets à `RadioButton` de ses écrans frères. Sous-libellés de KPI en `FontSize="9"`, en dessous du plancher documenté de 11px.

**`RecrutementView.xaml`** — Même motif systémique que Formation (styles de bouton/champ non utilisés). Le vrai CTA principal de l'écran (« Valider l'Embauche & Créer le Salarié ») est stylé en `SuccessBrush` (vert) plutôt qu'en accent teal, via style en ligne plutôt qu'une ressource partagée — probablement intentionnel (validation = succès) mais contourne complètement le système au lieu de définir un `SuccessButtonStyle` réutilisable. Onglets sans `FontSize`/`FontWeight` du tout — encore moins stylés que ceux de FormationView.

**`LoginWindow.xaml`** — Quasiment toute la fenêtre en hex/couleurs nommées codées en dur ; zéro usage de `GroundBrush`/`SurfaceBrush`/`InkBrush`. Champ utilisateur et mot de passe sans style — pas d'état de focus visible, sur le tout premier écran vu par l'utilisateur. Bouton « Mot de passe oublié ? » en `#0078D7` (bleu Windows) — couleur de marque différente du teal `#14675A` utilisé partout ailleurs dans l'app. **`Title="Connexion - IUTS Calculator"`** — encore l'ancien nom, alors que le rail de navigation affiche « Sigma Paie & RH » — incohérence de marque visible dans la barre de titre/la barre des tâches Windows.

**`PasswordResetWindow.xaml`** — Encore moins stylé que `LoginWindow` : labels de champ complètement non stylés, `TextBox`/`PasswordBox` sans la moindre `BorderBrush`. Le texte de statut d'erreur n'a pas `TextWrapping="Wrap"` — un message long pourrait être tronqué dans cette fenêtre de 380px de large. XAML mort en commentaire laissé dans le fichier. Pas de champ « confirmer le nouveau mot de passe », pas d'indication des exigences de mot de passe, pas de bouton retour vers la connexion.

**`DossierSelectionWindow.xaml`** — Le plus proche du système en usage de `Form*Style`, mais : `Nouveau`, `Modifier` **et Supprimer** utilisent tous les trois `MenuButtonStyle` (accent plein) côte à côte — le pire cas de non-respect de la règle « accent avec parcimonie » de tout l'audit, et Supprimer n'a aucune distinction visuelle d'action destructrice. « Enregistrer » et « Annuler » utilisent également tous deux `MenuButtonStyle` — Annuler stylé comme une action primaire au lieu de s'effacer visuellement. Utilise l'alias historique `GrayLightBrush` au lieu de `GroundBrush`, et une 3ᵉ variante non documentée de brush (`PrimaryBlueDarkerBrush`).

---

## Pistes de correction, par ordre de rentabilité

1. **Décider du sort de `PayslipMonthlyView.xaml` et `BulletinPaieView.xaml`** (suppression probable — code mort, non câblé, jamais mis à niveau).
2. **Ajouter 2 ressources manquantes à `App.xaml`** (`SuccessTintBrush`, `SuccessRuleBrush` sur le modèle de `WarningTintBrush`/`WarningBrush`) et les appliquer aux 3 badges « succès » codés en dur — corrige 3 fichiers d'un coup.
3. **Remplacer les 6 occurrences de `#F8FAFC`** par `SurfaceAltBrush` — mécanique, faible risque.
4. **Uniformiser tous les boutons « Supprimer »** sur `DangerButtonStyle` — mécanique, ~6 fichiers.
5. **Remettre à plat les fenêtres d'authentification** (Login/PasswordReset/DossierSelection) — le plus visible pour l'utilisateur puisque ce sont les tout premiers écrans, et le plus simple à isoler (3 fichiers, indépendants du reste de l'app).
6. **FormationView et RecrutementView** : remplacement systématique des boutons/champs en dur par les styles partagés, et alignement sur l'idiome d'onglets `RadioButton` des écrans RH frères. C'est le chantier le plus long (2 gros fichiers, très déviants), mais celui qui referme le plus grand écart de cohérence.
7. **Promouvoir les styles locaux dupliqués** (`StatCardStyle`, `TabHeaderBtnStyle`, `KpiCardStyle`) en ressources partagées dans `App.xaml` pour arrêter la dérive entre copies.
8. Le reste (DatePicker/TextBox non stylés isolés, boutons primaires multiples, alias historiques résiduels) peut être traité fichier par fichier selon la priorité métier de chaque écran.

Dites-moi par lequel commencer — je peux traiter les corrections mécaniques (points 2 à 4) en une seule passe, ou me concentrer d'abord sur les écrans les plus visibles (fenêtres de connexion, ou Formation/Recrutement).

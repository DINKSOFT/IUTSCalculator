# Audit du code livré — RH-05 « Recrutement & Candidatures »

Revue exhaustive du code réellement livré (`Models/Recrutement.cs`, `Services/RecrutementService.cs`, `Services/RecrutementExportService.cs`, `Services/SQLiteService.cs` [Migration33], `ViewModels/RecrutementViewModel.cs`, `Views/RecrutementView.xaml` + `.xaml.cs`, câblage `MainWindow.xaml`/`MainViewModel.cs`, `.csproj`, `RecrutementTests.cs`, `ETAT-DEVELOPPEMENT.md`), comparé point par point à ma critique du plan (`Analyse_Plan_RH-05.md`).

**Verdict global : implémentation très solide sur le plan métier — tous les points de ma critique ont été corrigés — mais 3 bugs de ressources XAML bloquent le lancement de l'application, dont un qui empêche même le démarrage du logiciel entier.**

---

## 🔴 1. Bugs bloquants — à corriger avant toute utilisation

### 1.1 `IconPeople` inexistant → l'application entière ne démarre plus

`Views/RecrutementView.xaml` (ligne 60) **et** `MainWindow.xaml` (ligne 703, le bouton de navigation « Recrutement & vivier ») référencent tous deux `{StaticResource IconPeople}`. Cette clé n'existe nulle part dans `App.xaml` (j'ai vérifié l'intégralité des ~50 clés `Icon*` déclarées) ni dans `Resources/DarkTheme.xaml`.

`StaticResource` est résolu immédiatement au chargement du XAML — une clé introuvable lève une `XamlParseException`. Comme la référence se trouve dans **`MainWindow.xaml`**, qui se charge au démarrage de l'application, **c'est tout le logiciel qui plantera au lancement**, pas seulement l'écran RH-05. C'est le bug le plus critique de cette livraison, plus grave que tout ce qui a été trouvé sur RH-03/RH-04.

**Correction** : ajouter dans `App.xaml`, à côté des autres `<sys:String x:Key="Icon...">` (lignes 119-148) :
```xml
<sys:String x:Key="IconPeople">&#xE716;</sys:String>
```
(ou réutiliser `IconEmploye`, déjà défini avec ce glyphe, dans les deux XAML au lieu de créer une nouvelle clé).

### 1.2 `IconCheckmark` inexistant

`Views/RecrutementView.xaml` (ligne 702, icône de la passerelle d'embauche SAL-01) référence `{StaticResource IconCheckmark}`. Seule `IconCheck` (`&#xE8FD;`) existe. Plantera dès l'ouverture de l'onglet « Entretiens & Embauche ».

**Correction** : renommer `IconCheckmark` → `IconCheck` dans `RecrutementView.xaml`.

### 1.3 `PrimaryDarkBrush` inexistant (5 occurrences)

Utilisé lignes 82, 130, 443, 571, 773 de `RecrutementView.xaml` (dont la carte KPI « POSTES À POURVOIR », visible dès l'ouverture de l'onglet par défaut). Aucune ressource de ce nom n'existe ; la plus proche est `PrimaryBlueDarkBrush` (définie ligne 109 de `App.xaml`). Plantera dès l'affichage du premier onglet.

**Correction** : remplacer les 5 occurrences de `PrimaryDarkBrush` par `PrimaryBlueDarkBrush`.

Ces trois clés manquantes reproduisent exactement le schéma d'incident déjà rencontré sur RH-03/RH-04 (clé de ressource inventée ou mal orthographiée) — je recommande d'en faire une vérification systématique (script de diff `x:Key` vs `StaticResource`) avant chaque livraison future, comme celle que j'ai faite ici.

---

## 🟠 2. Bug fonctionnel silencieux — type de contrat ignoré

Le formulaire d'embauche (`RecrutementView.xaml`, section SAL-01) propose un champ « Type de Contrat » lié à `TypeContratEmbaucheSaisi`, transmis à `IntegrerCandidatEnSalarieAsync(candidatureId, salaireNegocie, dateEntree, typeContrat, categorie, service)`.

Or dans `RecrutementService.cs`, le paramètre `typeContrat` **n'est jamais utilisé** (confirmé par recherche : une seule occurrence, dans la signature). Il n'est ni stocké (le modèle `Employee` n'a aucun champ de type de contrat), ni transmis à l'export PDF.

Conséquence concrète : dans `RecrutementExportService.GenererLettreEmbauchePdf`, la ligne « Nature du contrat » est **codée en dur** :
```csharp
AjouterLigneCondition(tableCond, "Nature du contrat :", "Contrat de Travail à Durée Indéterminée (CDI)");
```
Si un utilisateur RH embauche quelqu'un en CDD ou en stage et saisit « CDD » dans le formulaire, la lettre d'embauche officielle imprimée affirmera quand même « CDI » — un document légalement engageant avec une mention fausse.

**Correction recommandée** : transmettre `typeContrat` (et `candidature`/`Employee`) jusqu'à `GenererLettreEmbauchePdf` et l'interpoler à la place du texte fixe ; envisager aussi d'ajouter un champ « TypeContrat » à `Employee`/table `Employees` si ce type de contrat doit être conservé après l'embauche (actuellement perdu une fois le formulaire fermé).

---

## 🟡 3. Points de ma critique encore ouverts (déjà signalés comme « recommandés », non bloquants)

- **§2.1 Dédoublonnage du vivier** : `EnregistrerCandidatAsync` n'effectue toujours aucune vérification d'email/téléphone existant — un même candidat peut être saisi plusieurs fois. Non corrigé, comme attendu (c'était une recommandation, pas une correction obligatoire).
- **§2.2 Gestion physique des CV** : `CvChemin` reste une simple colonne texte, sans upload, extension autorisée ni nettoyage à la suppression. Non corrigé, idem.

Ni l'un ni l'autre ne bloquent l'usage courant du module ; à traiter dans une itération ultérieure si souhaité.

---

## ✅ 4. Ce qui a été corrigé — tous les points de ma critique du plan sont traités

| Point de la critique | Statut dans le code livré |
|---|---|
| §1.1 Génération du matricule (GUID, pas incrémentiel) | ✅ `Guid.NewGuid().ToString()` dans `IntegrerCandidatEnSalarieAsync` |
| §1.2 Réutilisation de `AddEmployeeAsync` (transaction composée) | ✅ méthode passée en `internal`, appelée avec `(conn, tx)` dans la même transaction que l'embauche |
| §1.3 Conversion monétaire `DbValue.MoneyToDb/FromDb` | ✅ appliquée systématiquement (RemunerationMin/Max, PretentionSalariale, SalaireNegocie) |
| §1.4 Verrou probatoire complet | ✅ étendu à `SupprimerCandidatAsync`, `AvancerEtapeCandidatureAsync`, `ChangerStatutCandidatureAsync`, `SupprimerCandidatureAsync`, `EnregistrerEntretienAsync`, `SupprimerEntretienAsync` — couverture complète via `VerifierCandidatureNonVerrouillee` |
| §1.5 Garde-fou anti-sur-recrutement | ✅ vérification `NombreRecrutes >= NombrePostes` / besoin `Pourvu` avant intégration |
| §2.3 Enum `EtapeSelection`/`TypeEtapeEntretien` dupliqués | ✅ unifiés en un seul enum `EtapeRecrutement` (8 valeurs), partagé par `Candidature` et `RecrutementEntretien` |
| §2.4 Re-candidature sur le même besoin | ✅ `PostulerAsync` réactive la candidature existante au lieu de planter sur la contrainte unique |
| §2.5 Citation légale | ✅ cohérente entre l'indice XAML et le PDF (Articles 38 à 43, Loi n° 028-2008/AN) — je ne peux pas certifier l'exactitude juridique du texte lui-même, mais l'incohérence interne que je redoutais n'existe pas |
| §2.6 Double ancrage migration + `SchemaCible` | ✅ `SchemaCible = 33`, `Migration33_Recrutement` appelée à la fois dans `CreerSchemaComplet()` et dans le chemin `if (version < 33)` |

Par ailleurs, points positifs non demandés dans ma critique mais vérifiés bons :
- **`RecrutementView.xaml`** n'a pas le bug de débordement de boutons découvert sur `FormationView.xaml` : les groupes de boutons utilisent des `WrapPanel`/`DockPanel`, et toute la page est enveloppée dans un `ScrollViewer` (horizontal + vertical) — pas de risque de boutons coupés.
- **Câblage** `MainWindow.xaml` (DataTemplate, bouton de nav, `DataTrigger`) et `MainViewModel.cs` (`IsNavRecrutementVisible`, `NavigateToRecrutementCommand`, entrée `NavMenuItemDefinition` avec mots-clés de recherche complets) : tout est correctement branché.
- **`.csproj`** (principal et tests) : toutes les entrées `Compile`/`Page` présentes, y compris `RecrutementView.xaml.cs` qui n'avait pas encore été chargé côté serveur au moment de mon audit (vérifié directement sur votre poste).
- **Tests** : 11 tests livrés (`RecrutementTests.cs`), au-delà des 10 promis — couvrant précisément les zones à risque : migration/amorçage, CRUD, réactivation de candidature, entretiens/recalcul de score, intégration atomique SAL-01, garde-fou anti-sur-recrutement, et les trois verrous probatoires (besoin, candidat, candidature) séparément, plus les exports.
- **`ETAT-DEVELOPPEMENT.md`** : entrée RH-05 fidèle à ce qui a été réellement livré.

---

## 5. Prochaine étape

Je peux appliquer immédiatement les 3 corrections de ressources XAML (§1) — ce sont des changements d'une ligne chacun, sans risque, et ils sont bloquants pour le lancement même du logiciel. Je peux aussi corriger le bug du type de contrat (§2) si vous voulez que je transmette `typeContrat` jusqu'au PDF dès maintenant, ou que je laisse cela pour plus tard.

Dites-moi si je procède.

# Analyse du plan d'implémentation — RH-05 « Recrutement & Candidatures »

Analyse effectuée à la lumière du code réel de Sigma Paie & RH (schéma `Employees`/`Postes`, `DataService.cs`, `DbValue.cs`, `SQLiteService.cs`) et de tout ce qui a été observé sur RH-03 et RH-04 dans cette conversation : absence de `PRAGMA foreign_keys`, verrous d'immutabilité oubliés sur des méthodes annexes, conventions de stockage monétaire, double point d'ancrage des migrations.

---

## 0. Bonne nouvelle de départ : le schéma existant est déjà prêt

Contrairement à ce qu'on aurait pu craindre, **`Employees` et `Postes` possèdent déjà toutes les colonnes dont RH-05 a besoin** — aucune migration sur ces deux tables n'est nécessaire :
- `Employees.PosteId`, `Employees.ManagerMatricule` existent (ajoutées via `AjouterColonneSiAbsente` lors de la migration Postes/RH-01).
- `Postes.TitulaireMatricule` existe (`FOREIGN KEY(TitulaireMatricule) REFERENCES Employees(EmployeeId)`), c'est le champ déjà utilisé par `OrganigrammeService`/`OrganigrammeViewModel` pour désigner l'occupant d'un poste dans l'organigramme.

Le plan peut donc s'appuyer sur ces champs tels quels. C'est un point de conception solide qu'il ne faut pas casser en réinventant des colonnes parallèles.

---

## 1. Corrections nécessaires avant construction

### 1.1 — Génération du matricule : le plan invente une convention qui n'existe pas (CRITIQUE)

Le plan dit : *« Détermine le nouveau matricule unique (ex: prochain matricule incrémentiel ou format fourni) »*.

Ce n'est pas ce que fait l'application aujourd'hui. `DataService.AddEmployeeAsync` fait ceci :
```csharp
if (string.IsNullOrEmpty(employee.EmployeeId))
    employee.EmployeeId = Guid.NewGuid().ToString();
```
**Le matricule réel de production (`EmployeeId`, clé primaire de `Employees`) est un GUID**, pas un code séquentiel du style « EMP0043 » (ces derniers ne sont que des données de test dans `FormationTests.cs`/`AbsenceBatchTests.cs`, jamais la vraie logique). Si `IntegrerCandidatEnSalarieAsync` invente son propre schéma de matricule « incrémentiel », les salariés créés via RH-05 auront un format d'identifiant différent de ceux créés via l'écran classique de gestion des salariés — source de confusion et de bugs potentiels partout où du code suppose implicitement un format de matricule.

**Recommandation** : ne pas fabriquer de nouveau matricule ; laisser `EmployeeId` vide et s'appuyer sur `DataService` pour le générer, exactement comme le fait déjà le reste de l'application.

### 1.2 — Ne pas dupliquer l'INSERT dans `Employees`

Le plan décrit « insère l'enregistrement complet dans la table `Employees` » depuis `RecrutementService`, alors qu'il existe déjà `DataService.AddEmployeeAsync(Employee)` qui gère les ~30 colonnes (dont beaucoup absentes du plan : `NumeroCnib`, `SituationMatrimoniale`, `ConventionId`, etc.). Réimplémenter un second INSERT en parallèle dans `RecrutementService` crée un point de divergence : le jour où `Employees` gagne une colonne, il faudra penser à la mettre à jour aux **deux** endroits.

**Point d'architecture à trancher explicitement** : `AddEmployeeAsync(employee, conn, tx)` (la variante qui participe à une transaction externe) est `private` dans `DataService.cs`. Pour que `IntegrerCandidatEnSalarieAsync` puisse créer le salarié **dans la même transaction atomique** que la mise à jour du besoin/de la candidature (ce que le plan promet), il faut soit :
- rendre cette surcharge `internal`/`public` pour que `RecrutementService` puisse la réutiliser avec sa propre connexion/transaction, soit
- assumer et documenter que l'insertion du salarié se fait dans une transaction séparée de la mise à jour du besoin (moins atomique — un correctif de rattrapage doit être prévu si l'une réussit et l'autre échoue).

Ne pas trancher cette question au moment du build, ça se découvre généralement après coup, en pleine transaction.

### 1.3 — Convention monétaire : `INTEGER` brut au lieu du format centimes établi

`Employees.BaseSalary` est stocké via `DbValue.MoneyToDb` :
```csharp
public static long MoneyToDb(decimal montant) => (long)decimal.Round(montant * 100m, 0, ...);
```
C'est-à-dire que la base stocke des **centimes** (×100), alors que les propriétés C# exposent des `decimal` en FCFA. C'est la même convention que `FormationCatalogue.CoutEstimeParParticipant` dans RH-04.

Le plan déclare `RemunerationProposeeMin/Max` et `SalaireNegocie` en `INTEGER` sans préciser cette conversion. Si le modèle C# (`BesoinRecrutement`, `Candidature`) expose ces champs comme `decimal` (ce qui est cohérent avec le reste de l'app) **il faut explicitement passer par `DbValue.MoneyToDb`/`MoneyFromDb` en lecture/écriture**, sinon on se retrouve avec des montants ×100 par rapport à `Employees.BaseSalary` au moment où `SalaireNegocie` alimente `BaseSalary` lors de l'embauche — un bug silencieux et difficile à repérer (le salarié serait payé 100× trop, ou 100× trop peu).

### 1.4 — Verrou probatoire incomplet

Le plan protège la suppression de la **candidature** et du **besoin** une fois `SalarieCreeMatricule` renseigné (test n°8). Deux trous restent :

- **`SupprimerCandidatAsync`** (suppression du profil dans le *vivier*, pas d'une candidature précise) n'est pas mentionné dans la section verrou. Or le FK déclaré est `ON DELETE CASCADE` sur `CandidatId` — si la cascade C# de `SupprimerCandidatAsync` reproduit cette cascade sans vérifier qu'aucune des candidatures de ce candidat n'a `SalarieCreeMatricule != null`, on peut supprimer d'un clic le profil vivier d'un salarié activement embauché (l'entretien, l'historique de recrutement disparaissent, alors que la fiche salarié, elle, reste). Il faut le même verrou qu'au niveau besoin/candidature, appliqué ici aussi.
- **Modification après embauche** : rien n'empêche `AvancerEtapeCandidatureAsync`/`ChangerStatutCandidatureAsync` de continuer à modifier une candidature dont le statut est déjà `Embauchee`. C'est exactement le type de trou trouvé et corrigé sur RH-03 (`ChangerStatutEntretienAsync`/`AssignerEvaluateurAsync` ne vérifiaient pas le verrou que `SupprimerEntretienAsync` vérifiait déjà). Recommandation : appliquer le même garde-fou ici dès la conception, plutôt que de le découvrir après livraison.

### 1.5 — Pas de garde-fou anti-sur-recrutement

Le plan fait passer `Besoin.Statut` à `Pourvu` quand `NombreRecrutes >= NombrePostes`, mais ne dit nulle part que `IntegrerCandidatEnSalarieAsync` **refuse** une nouvelle embauche si le besoin est déjà `Pourvu` ou `Annulé`. Sans ce contrôle explicite, rien n'empêche d'embaucher 5 personnes sur un besoin qui n'en demandait que 2 — `NombreRecrutes` grimpe au-delà de `NombrePostes` sans qu'aucune erreur ne remonte.

---

## 2. Ajouts à envisager

### 2.1 — Dédoublonnage du vivier, pas seulement des candidatures

L'index unique `UX_RecrutementCandidatures_Besoin_Candidat` empêche un même candidat de postuler deux fois au **même** besoin — mais rien n'empêche de créer deux fiches `RecrutementCandidats` distinctes pour la **même personne** (candidature spontanée en janvier, puis nouvelle candidature en juin sans recherche préalable dans le vivier). Sur la durée, le vivier va se remplir de doublons. Recommandation : au minimum, une recherche par email/téléphone avant création d'un nouveau `Candidat`, avec proposition de réutiliser la fiche existante plutôt que d'en créer une nouvelle.

### 2.2 — Gestion physique du CV (`CVFichierChemin`)

C'est la première fois dans ce projet qu'un module gère un **fichier apporté par l'utilisateur** (jusqu'ici, RH-03/RH-04 ne font que produire des PDF/Excel en sortie). Le plan ne précise pas : où le fichier est physiquement copié (dossier dédié sous les données de l'application ?), les extensions acceptées, une limite de taille, et surtout — que devient le fichier quand `SupprimerCandidatAsync` est appelé ? Sans nettoyage explicite, des CV orphelins s'accumuleront sur le disque indéfiniment.

### 2.3 — Deux énumérations qui se recouvrent avec des valeurs différentes

`EtapeSelection` (sur `Candidature.EtapeActuelle`) et `TypeEtapeEntretien` (sur `RecrutementEntretien.TypeEtape`) décrivent en grande partie la même chose mais avec des valeurs numériques différentes pour des concepts équivalents :

| Concept | `EtapeSelection` | `TypeEtapeEntretien` |
|---|---|---|
| Tri CV | `Reception` (0) / `Preselection` (1) | `TriCV` (0) |
| Test technique | `TestTechnique` (2) | `TestEcritOuTechnique` (1) |
| Entretien RH | `EntretienRH` (3) | `EntretienRH` (2) |
| Entretien Manager | `EntretienManager` (4) | `EntretienManagerN1` (3) |
| — | — | `EntretienDirection` (4) *(sans équivalent stage)* |

Deux enums proches avec des valeurs numériques décalées, c'est exactement le terrain sur lequel RH-04 a buté (une valeur de statut supposée exister mais absente ailleurs dans le code). Recommandation : soit fusionner en une seule énumération réutilisée aux deux endroits, soit documenter explicitement, en commentaire, la correspondance attendue entre les deux — pour que personne n'ait à deviner laquelle correspond à laquelle en cours d'implémentation.

### 2.4 — Re-candidature sur un besoin déjà tenté

Si un candidat rejeté sur un besoin est reconsidéré plus tard pour ce **même** besoin (toujours ouvert), l'index unique `UX_RecrutementCandidatures_Besoin_Candidat` va bloquer un second `INSERT` — et si rien n'intercepte ça en amont, l'utilisateur verra remonter une exception SQLite brute plutôt qu'un message clair. Il vaut mieux que `CreerCandidatureAsync` vérifie d'abord l'existence d'une ligne `(BesoinId, CandidatId)`, et propose de réactiver/mettre à jour celle-ci plutôt que de tenter un nouvel `INSERT`.

### 2.5 — Référence légale à vérifier avant impression

La lettre d'embauche cite « Loi n°028-2008/AN » et des articles précis (40-42) sur la période d'essai. C'est un document à valeur légale que l'entreprise remettra à un salarié — la référence et les articles doivent être vérifiés auprès du Code du travail burkinabè en vigueur avant d'être figés dans le PDF généré, plutôt que pris tels quels du plan.

### 2.6 — Rappel méthodologique (déjà vu deux fois dans cette conversation)

Pour la Migration 33 : penser aux **deux** points d'ancrage — l'appel dans le chemin de mise à niveau (`if (version < 33) { Migration33_Recrutement(connection); EnregistrerVersion(connection, 33); }`) **et** l'appel direct dans `CreerSchemaComplet()` pour une base neuve — plus la mise à jour de la constante `SchemaCible = 33` (actuellement `32`). C'est un endroit où RH-04 avait fait les choses correctement ; à reproduire à l'identique.

---

## 3. Ce qui est déjà solide

- Le choix de s'appuyer sur les colonnes existantes de `Employees`/`Postes` plutôt que d'en recréer (§0).
- L'index unique anti-doublon au niveau candidature, la logique de cascade C# assumée (cohérente avec l'absence de `PRAGMA foreign_keys`), et le verrou de suppression après embauche (même s'il doit être étendu, §1.4).
- La séparation claire besoin / candidat / candidature / entretien, qui correspond bien au cycle de recrutement réel.
- Le double export réglementaire (lettre A4 + bilan Excel 4 feuilles) dans la continuité directe des conventions déjà posées par RH-03/RH-04.
- Les 10 tests couvrent déjà les scénarios les plus sensibles (anti-doublon, verrou probatoire, cascade, intégration salarié, bascule automatique du statut) — il suffit d'y ajouter la couverture des points 1.4/1.5/2.1/2.4 ci-dessus.

---

## 4. Prochaine étape suggérée

Avant le développement, trancher explicitement les points 1.1 (génération du matricule), 1.2 (réutilisation ou non de `DataService.AddEmployeeAsync` et son passage en `internal`), 1.3 (conversion monétaire) et 2.3 (fusion ou correspondance documentée des deux énumérations d'étapes) — ce sont les quatre décisions qui, si elles ne sont pas prises maintenant, se traduiront presque certainement en bugs silencieux ou en crash runtime une fois le code livré, sur le modèle de ce qui a déjà été observé sur RH-03 et RH-04.

# Modèle d'importation CSV des employés — Sigma Paie & RH

Ce fichier accompagne `Modele_Import_Employes.csv`, le modèle à remplir pour importer des
employés dans **Gestion des employés → Importer des employés**.

## Format du fichier

- **Séparateur** : point-virgule (`;`) — le format qu'utilise déjà un Excel réglé en
  français (la virgule y sert de séparateur décimal, elle ne peut donc pas servir de
  séparateur de colonnes).
- **Encodage** : UTF-8 avec BOM. Ouvrir, modifier et réenregistrer le modèle dans Excel
  conserve automatiquement ce réglage — inutile d'y toucher.
- **Dates** : format `JJ/MM/AAAA` (ex. `15/03/1998`).
- **Montants** : en francs CFA, sans espace ni séparateur de milliers (ex. `120000`, pas
  `120 000`). Une valeur avec virgule décimale (`120000,50`) est acceptée.
- L'ordre des colonnes peut être changé sans problème : l'import lit les colonnes par
  leur **nom** d'en-tête, pas par leur position. Ne pas renommer les en-têtes.

## Comportement de l'import — AJOUT / MISE À JOUR, jamais de suppression

- **Matricule déjà présent dans le dossier** → la fiche de cet employé est **entièrement
  remplacée** par le contenu de la ligne (mêmes règles que le bouton « Modifier » de
  l'écran salarié). Ne pas lister un employé qu'on ne veut pas écraser.
- **Matricule vide** → l'import essaie d'abord de reconnaître un employé déjà existant
  par **Nom + Prénom** avant de créer une nouvelle fiche. C'est ce qui permet de
  réimporter le même fichier (corrigé) plusieurs fois sans créer de doublons, même sans
  y recopier le matricule généré par l'application.
  - Si un seul employé du dossier porte ce Nom + Prénom → sa fiche est mise à jour.
  - Si **plusieurs** employés du dossier partagent exactement ce Nom + Prénom (des
    homonymes) → l'import ne devine pas lequel mettre à jour : une **nouvelle** fiche
    est créée à la place, avec un avertissement. Dans ce cas précis, il faut renseigner
    le Matricule (visible dans Gestion des employés) pour viser le bon homonyme.
  - Si aucun employé ne porte ce Nom + Prénom → un nouvel employé est créé, comme
    attendu pour une vraie nouvelle recrue.
- Un employé du dossier absent du fichier CSV n'est **jamais supprimé**.

## Colonnes

| Colonne | Obligatoire | Contenu |
|---|---|---|
| Matricule | non | Laisser vide pour une nouvelle recrue, ou pour laisser l'import retrouver l'employé par Nom + Prénom (voir ci-dessus). Renseigner un matricule existant pour cibler précisément cette fiche, notamment en cas d'homonymes. |
| Nom | **oui** | Nom de famille. |
| Prenom | **oui** | Prénom. |
| Sexe | non | Masculin / Féminin. |
| DateNaissance | non | JJ/MM/AAAA. |
| LieuNaissance | non | |
| Nationalite | non | |
| SituationMatrimoniale | non | Célibataire / Marié(e) / Divorcé(e) / Veuf(ve), texte libre. |
| NombreEnfantsACharge | non | Nombre entier (défaut 0). |
| ConjointTravaille | non | Oui / Non (défaut Non). |
| NumeroCNIB | non | |
| DateDelivranceCNIB | non | JJ/MM/AAAA. |
| LieuDelivranceCNIB | non | |
| Adresse | non | |
| Telephone | non | |
| Email | non | |
| NumeroCNSS | non | Matricule CNSS — nécessaire pour la déclaration CNSS. |
| NumeroIFU | non | IFU personnel du salarié. |
| Categorie | non | Ex. « Cadre Supérieur », « Cadre Inférieur », ou tout autre libellé de catégorie déjà utilisé dans le dossier. |
| Fonction | non | Poste occupé. |
| DateEntree | non | JJ/MM/AAAA. Recommandé : sert au calcul de l'ancienneté. |
| SalaireBase | non | Salaire de base mensuel, en FCFA. |
| IndemniteLogement | non | |
| IndemniteTransport | non | |
| IndemniteFonction | non | |
| IndemniteSujetion | non | |
| Banque | non | |
| NumeroCompte | non | Numéro de compte ou IBAN. |
| ModeReglement | non | Virement / Espèces / Chèque / Mobile Money. Un règlement par « Virement » sans NumeroCompte sera signalé par l'écran des virements. |
| ConventionCollective | non | Code ou libellé d'une convention déjà créée dans le dossier (écran Conventions collectives). Non reconnue → ignorée, avec avertissement. |
| Echelon | non | Échelon de la grille de la convention choisie (texte libre, ex. « A », « B »). |
| Statut | non | Actif / Sorti / Suspendu (défaut Actif). |
| DateSortie | non | JJ/MM/AAAA — pertinent seulement si Statut = Sorti. |
| MotifDepart | non | Ex. Démission, Licenciement, Retraite, Fin de contrat. |

Un champ non listé ici (ex. `PensionContribution`) n'existe pas dans le modèle
volontairement : c'est une valeur recalculée par le moteur de paie à chaque bulletin, pas
une donnée à saisir pour un employé.

## Après l'import

Un résumé s'affiche (« X ajouté(s), Y mis à jour »). Si une ligne pose problème :

- **Erreur** (ligne ignorée, à corriger et réimporter) : Nom/Prénom manquants, ou un
  montant illisible (SalaireBase, indemnités).
- **Avertissement** (ligne importée quand même, mais un champ précis a été ignoré ou
  ramené à sa valeur par défaut) : date illisible, ConjointTravaille ou Statut non
  reconnu, ConventionCollective introuvable, colonne d'en-tête non reconnue.

Le détail complet (avec le numéro de ligne du fichier) s'affiche dans une fenêtre après
l'import dès qu'au moins une erreur ou un avertissement existe.

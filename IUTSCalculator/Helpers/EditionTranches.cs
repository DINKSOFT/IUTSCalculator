using System.Collections.ObjectModel;
using System.Linq;
using IUTSCalculator.Models;

namespace IUTSCalculator.Helpers
{
    /// <summary>
    /// Édition d'une liste de tranches de barème (ajout, suppression, recollage des
    /// bornes) : le même geste qu'utilisent à la fois l'écran « Barèmes légaux »
    /// (BaremeViewModel, sur le barème sélectionné dans sa liste) et l'écran
    /// « Paramètres » (SettingsViewModel, sur le barème actuellement en vigueur) —
    /// tous deux éditent une <see cref="ObservableCollection{TrancheBareme}"/> d'un
    /// <see cref="BaremeLegal"/>. Centralisé ici pour que ce geste délicat (recoller les
    /// bornes sans laisser de trou ni de chevauchement) n'existe pas en deux versions qui
    /// pourraient diverger avec le temps.
    /// </summary>
    public static class EditionTranches
    {
        /// <summary>
        /// Ajoute une tranche juste avant la tranche ouverte, en partant de sa borne
        /// basse — c'est ainsi que s'ajoute un palier dans un barème réel. Retourne la
        /// tranche créée pour que l'appelant la sélectionne dans sa grille.
        /// </summary>
        public static TrancheBareme Ajouter(ObservableCollection<TrancheBareme> tranches)
        {
            var ouverte = tranches.LastOrDefault(t => t.EstDerniereTranche);

            decimal depart = ouverte != null
                ? ouverte.BorneInferieure
                : (tranches.Count > 0 ? tranches.Max(t => t.BorneSuperieure) : 0m);

            var nouvelle = new TrancheBareme
            {
                BorneInferieure = depart,
                BorneSuperieure = depart,
                Taux = 0m
            };

            if (ouverte != null)
            {
                int position = tranches.IndexOf(ouverte);
                tranches.Insert(position, nouvelle);
                ouverte.BorneInferieure = depart;
            }
            else
            {
                tranches.Add(nouvelle);
            }

            Renumeroter(tranches);
            return nouvelle;
        }

        /// <summary>
        /// Retire une tranche et recolle les bornes voisines : sans cela, la largeur de
        /// la tranche supprimée ne serait imposée nulle part.
        /// </summary>
        public static void Supprimer(ObservableCollection<TrancheBareme> tranches, TrancheBareme cible)
        {
            if (tranches == null || cible == null) return;

            int position = tranches.IndexOf(cible);
            if (position < 0) return;

            tranches.Remove(cible);

            if (position > 0 && position < tranches.Count)
                tranches[position].BorneInferieure = tranches[position - 1].BorneSuperieure;
            else if (position == 0 && tranches.Count > 0)
                tranches[0].BorneInferieure = 0m;

            Renumeroter(tranches);
        }

        /// <summary>
        /// Renumérote les tranches et recolle leurs bornes.
        ///
        /// La borne inférieure n'est jamais saisie : elle vaut, par construction, la borne
        /// supérieure de la tranche précédente. Une grille l'affiche en lecture seule pour
        /// cette raison — on ne peut pas se tromper sur une valeur qu'on ne tape pas, et un
        /// trou de 100 FCFA entre deux paliers passerait inaperçu.
        /// </summary>
        public static void Renumeroter(ObservableCollection<TrancheBareme> tranches)
        {
            if (tranches == null) return;

            int ordre = 1;
            decimal precedente = 0m;

            foreach (var tranche in tranches)
            {
                tranche.Ordre = ordre;
                tranche.BorneInferieure = precedente;

                if (!tranche.EstDerniereTranche)
                    precedente = tranche.BorneSuperieure;

                ordre++;
            }
        }
    }
}

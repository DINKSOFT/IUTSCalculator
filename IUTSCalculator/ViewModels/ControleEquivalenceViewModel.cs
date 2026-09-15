using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using IUTSCalculator.Helpers;
using IUTSCalculator.Services;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Écran « Contrôle du moteur » (module DEC-01).
    ///
    /// Sa raison d'être tient en une phrase : DEC-01 a touché à un calcul audité, et un
    /// calcul audité ne se modifie pas sur parole. Cet écran rejoue chaque bulletin avec
    /// l'ancienne formule et la nouvelle, et affiche l'écart — zéro, tant qu'aucun barème
    /// ne change réellement un taux.
    ///
    /// Il reste utile ensuite : le jour où un barème modifie un taux, c'est ici qu'on voit
    /// salarié par salarié ce que ce changement produit, avant de recalculer quoi que ce
    /// soit.
    /// </summary>
    public class ControleEquivalenceViewModel : BaseViewModel
    {
        private readonly ControleEquivalenceService _controleService;

        public ObservableCollection<LigneControleEquivalence> Lignes { get; } =
            new ObservableCollection<LigneControleEquivalence>();

        public ControleEquivalenceViewModel(ControleEquivalenceService controleService)
        {
            if (controleService == null) throw new ArgumentNullException("controleService");
            _controleService = controleService;

            Title = "Contrôle du moteur";

            ControlerCommand = new RelayCommand(async () => await ControlerAsync(), () => !IsBusy);
        }

        public ICommand ControlerCommand { get; private set; }

        private bool _controleEffectue;
        public bool ControleEffectue
        {
            get { return _controleEffectue; }
            set { SetProperty(ref _controleEffectue, value); }
        }

        public int NombreControle
        {
            get { return Lignes.Count; }
        }

        public int NombreIdentiques
        {
            get { return Lignes.Count(l => l.EstIdentique); }
        }

        public int NombreEcarts
        {
            get { return Lignes.Count(l => !l.EstIdentique); }
        }

        public bool ToutEstIdentique
        {
            get { return ControleEffectue && Lignes.Count > 0 && NombreEcarts == 0; }
        }

        public bool ADesEcarts
        {
            get { return ControleEffectue && NombreEcarts > 0; }
        }

        private string _parametresLibelle;
        public string ParametresLibelle
        {
            get { return _parametresLibelle; }
            set { SetProperty(ref _parametresLibelle, value); }
        }

        private string _verdict;
        /// <summary>Phrase de conclusion, celle que l'utilisateur lira en premier.</summary>
        public string Verdict
        {
            get { return _verdict; }
            set { SetProperty(ref _verdict, value); }
        }

        private async Task ControlerAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Recalcul des bulletins avec les deux formules...";

                var resultats = await _controleService.ControlerAsync();

                Lignes.Clear();
                foreach (var ligne in resultats)
                    Lignes.Add(ligne);

                var parametres = _controleService.ParametresAppliques;
                ParametresLibelle = string.Format("{0} — {1}",
                    parametres.Designation, parametres.Ecarts());

                ControleEffectue = true;
                RafraichirTotaux();

                if (Lignes.Count == 0)
                {
                    Verdict = "Aucun salarié dans le dossier : il n'y a rien à comparer.";
                    StatusMessage = Verdict;
                    return;
                }

                if (NombreEcarts == 0)
                {
                    Verdict = string.Format(
                        "{0} bulletins recalculés, {0} identiques au franc près. " +
                        "Le passage aux barèmes datés n'a modifié aucun résultat.",
                        Lignes.Count);
                }
                else
                {
                    Verdict = string.Format(
                        "{0} bulletins sur {1} donnent un résultat différent. " +
                        "Si vous n'avez pas modifié de taux dans les barèmes, ce n'est pas normal : " +
                        "ne validez rien et signalez-le.",
                        NombreEcarts, Lignes.Count);
                }

                StatusMessage = Verdict;
            }
            catch (Exception ex)
            {
                ControleEffectue = false;
                StatusMessage = "Erreur pendant le contrôle : " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RafraichirTotaux()
        {
            OnPropertyChanged(nameof(NombreControle));
            OnPropertyChanged(nameof(NombreIdentiques));
            OnPropertyChanged(nameof(NombreEcarts));
            OnPropertyChanged(nameof(ToutEstIdentique));
            OnPropertyChanged(nameof(ADesEcarts));
        }
    }
}

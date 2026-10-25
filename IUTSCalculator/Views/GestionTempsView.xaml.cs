using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Écran « Gestion des temps &amp; rotations » (RH-08).
    ///
    /// Le planning a une colonne par jour de la période de pointage, dont le nombre varie
    /// (28 à 31 jours, et la période peut être décalée du mois civil) : ces colonnes sont
    /// construites ici, à chaque changement de période, plutôt que déclarées en XAML.
    /// Chaque colonne lie la case <c>Jours[i]</c> de la ligne : le ViewModel ne connaît
    /// pas la vue, il signale seulement que les dates ont changé.
    /// </summary>
    public partial class GestionTempsView : UserControl
    {
        private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        private GestionTempsViewModel _vm;
        private readonly List<DataGridColumn> _colonnesJours = new List<DataGridColumn>();

        public GestionTempsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
                _vm.ColonnesPlanningChangees -= OnColonnesChangees;

            _vm = DataContext as GestionTempsViewModel;

            if (_vm != null)
            {
                _vm.ColonnesPlanningChangees += OnColonnesChangees;
                ConstruireColonnes();
            }
        }

        private void OnColonnesChangees(object sender, EventArgs e)
        {
            if (Dispatcher.CheckAccess()) ConstruireColonnes();
            else Dispatcher.Invoke(ConstruireColonnes);
        }

        private void ConstruireColonnes()
        {
            foreach (var c in _colonnesJours)
                GrillePlanning.Columns.Remove(c);
            _colonnesJours.Clear();

            if (_vm == null) return;

            var feries = new HashSet<DateTime>();
            foreach (var f in _vm.Feries) feries.Add(f.Date.Date);

            var fr = CultureInfo.GetCultureInfo("fr-FR");
            int position = 3; // après Matricule, Salarié, Équipe ; « Postes » reste en dernier

            for (int i = 0; i < _vm.DatesPeriode.Count; i++)
            {
                var date = _vm.DatesPeriode[i];
                var colonne = new DataGridTemplateColumn
                {
                    Header = EnTete(date, feries.Contains(date), fr),
                    Width = new DataGridLength(32),
                    MinWidth = 28,
                    CanUserSort = false,
                    CanUserResize = false,
                    CellTemplate = Modele(i, false),
                    CellEditingTemplate = Modele(i, true)
                };

                GrillePlanning.Columns.Insert(position + i, colonne);
                _colonnesJours.Add(colonne);
            }
        }

        private static object EnTete(DateTime date, bool ferie, CultureInfo fr)
        {
            string jour = fr.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek);
            if (jour.Length > 2) jour = jour.Substring(0, 2);

            var pile = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            pile.Children.Add(new TextBlock
            {
                Text = jour,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            pile.Children.Add(new TextBlock
            {
                Text = date.Day.ToString(CultureInfo.InvariantCulture),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            if (ferie || date.DayOfWeek == DayOfWeek.Sunday)
            {
                foreach (TextBlock t in pile.Children)
                    t.Foreground = ferie ? Brushes.DarkOrange : Brushes.Firebrick;
            }

            pile.ToolTip = date.ToString("dddd dd MMMM yyyy", fr) + (ferie ? " — férié" : string.Empty);
            return pile;
        }

        /// <summary>Modèle d'une case : pastille colorée en lecture, zone de saisie du code en édition.</summary>
        private static DataTemplate Modele(int index, bool edition)
        {
            string chemin = "Jours[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            string xaml = edition
                ? "<DataTemplate xmlns='" + Presentation + "'>" +
                  "<TextBox Text='{Binding " + chemin + ".Code, UpdateSourceTrigger=LostFocus}' CharacterCasing='Upper' MaxLength='2' " +
                  "HorizontalContentAlignment='Center' VerticalContentAlignment='Center' BorderThickness='0' Padding='0' FontWeight='SemiBold'/>" +
                  "</DataTemplate>"
                : "<DataTemplate xmlns='" + Presentation + "'>" +
                  "<Border Background='{Binding " + chemin + ".Fond, Mode=OneWay}' ToolTip='{Binding " + chemin + ".Infobulle, Mode=OneWay}'>" +
                  "<TextBlock Text='{Binding " + chemin + ".Code, Mode=OneWay}' HorizontalAlignment='Center' VerticalAlignment='Center' " +
                  "FontWeight='SemiBold' FontSize='11'/></Border>" +
                  "</DataTemplate>";

            return (DataTemplate)XamlReader.Parse(xaml);
        }

        /// <summary>
        /// À l'ouverture d'une case en édition : la zone de saisie prend le focus, et la
        /// lettre tapée pour ouvrir la case n'est pas perdue (taper « N » sur une case
        /// sélectionnée y écrit N, comme dans Excel).
        /// </summary>
        private void GrillePlanning_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            var saisie = e.EditingEventArgs as TextCompositionEventArgs;
            string texte = saisie != null ? saisie.Text : null;
            var element = e.EditingElement;

            // Le modèle d'édition n'est appliqué qu'au prochain passage de mise en page.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var zone = TrouverEnfant<TextBox>(element);
                if (zone == null) return;

                zone.Focus();
                Keyboard.Focus(zone);
                if (!string.IsNullOrEmpty(texte))
                {
                    zone.Text = texte.ToUpperInvariant();
                    zone.CaretIndex = zone.Text.Length;
                }
                else
                {
                    zone.SelectAll();
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // ── Défilement dans les jours de la période ──────────────────────

        /// <summary>Le ScrollViewer interne de la grille (celui qui porte la barre horizontale).</summary>
        private ScrollViewer DefilementGrille()
        {
            return TrouverEnfant<ScrollViewer>(GrillePlanning);
        }

        /// <summary>Maj + molette fait défiler les jours horizontalement, comme dans Excel.</summary>
        private void GrillePlanning_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;
            var defilement = DefilementGrille();
            if (defilement == null) return;
            defilement.ScrollToHorizontalOffset(defilement.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private void AllerAuDebut_Click(object sender, RoutedEventArgs e)
        {
            DefilementGrille()?.ScrollToLeftEnd();
        }

        private void AllerALaFin_Click(object sender, RoutedEventArgs e)
        {
            DefilementGrille()?.ScrollToRightEnd();
        }

        /// <summary>
        /// Amène la colonne du jour à l'écran. Les trois premières colonnes (matricule, salarié,
        /// équipe) sont figées : le décalage horizontal se compte à partir du premier jour.
        /// </summary>
        private void AllerAujourdhui_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            int index = _vm.DatesPeriode.IndexOf(DateTime.Today);
            if (index < 0 || index >= _colonnesJours.Count)
            {
                _vm.StatusMessage = "Aujourd'hui n'est pas dans la période de pointage affichée.";
                return;
            }

            var defilement = DefilementGrille();
            if (defilement == null) return;

            double decalage = 0;
            for (int i = 0; i < index; i++)
                decalage += _colonnesJours[i].ActualWidth;

            // Deux jours de contexte à gauche de la date du jour.
            double largeur = _colonnesJours[index].ActualWidth;
            defilement.ScrollToHorizontalOffset(Math.Max(0, decalage - 2 * largeur));
        }

        // ── Remplissage d'une plage de cases ─────────────────────────────

        private void AppliquerSelection_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            AppliquerALaSelection(_vm.CodeRemplissage);
        }

        /// <summary>Suppr, hors saisie dans une case, vide toutes les cases sélectionnées.</summary>
        private void GrillePlanning_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete || _vm == null) return;
            if (Keyboard.FocusedElement is TextBox) return; // une case est en cours de saisie
            if (GrillePlanning.SelectedCells.Count == 0) return;

            AppliquerALaSelection(string.Empty);
            e.Handled = true;
        }

        private void AppliquerALaSelection(string code)
        {
            // Une case en cours d'édition est d'abord validée, sinon sa saisie écraserait le remplissage.
            GrillePlanning.CommitEdit(DataGridEditingUnit.Cell, true);

            var cellules = new List<CellulePlanning>();
            foreach (var info in GrillePlanning.SelectedCells)
            {
                var ligne = info.Item as LignePlanning;
                if (ligne == null) continue;
                int index = _colonnesJours.IndexOf(info.Column);
                if (index < 0 || index >= ligne.Jours.Count) continue;
                cellules.Add(ligne.Jours[index]);
            }

            if (cellules.Count == 0)
            {
                _vm.StatusMessage = "Sélectionnez d'abord des cases de jours dans le planning.";
                return;
            }

            _vm.AppliquerCode(cellules, code);
        }

        private static T TrouverEnfant<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            var trouve = parent as T;
            if (trouve != null) return trouve;

            int n = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < n; i++)
            {
                var r = TrouverEnfant<T>(VisualTreeHelper.GetChild(parent, i));
                if (r != null) return r;
            }
            return null;
        }
    }
}

using System;
using System.IO;
using System.Windows;
using IUTSCalculator.Services;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Fenêtre affichée pendant les travaux de démarrage (chiffrement des bases) : elle montre
    /// l'étape en cours et une barre qui se remplit base après base.
    /// </summary>
    public partial class DemarrageWindow : Window
    {
        public DemarrageWindow()
        {
            InitializeComponent();
        }

        /// <summary>Étape en cours, avec une barre indéterminée tant que l'avancement n'est pas connu.</summary>
        public void AfficherEtape(string etape)
        {
            Etape.Text = etape;
            Detail.Text = "";
            Barre.IsIndeterminate = true;
        }

        /// <summary>Avancement du chiffrement : remplit la barre au rythme des bases traitées.</summary>
        public void AfficherAvancement(AvancementChiffrement avancement)
        {
            if (avancement.Total == 0) return;

            Etape.Text = "Sécurisation des données (" + Math.Min(avancement.Traitees + 1, avancement.Total)
                         + " sur " + avancement.Total + ")…";
            Detail.Text = avancement.Base != null ? Path.GetFileName(avancement.Base) : "";
            Barre.IsIndeterminate = false;
            Barre.Maximum = avancement.Total;
            Barre.Value = avancement.Traitees;
        }
    }
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace IUTSCalculator.ViewModels
{
    /// <summary>
    /// Un mois dans la plage choisie pour une génération de bulletins sur plusieurs mois,
    /// avec sa propre case à cocher : la plage donne le cadre, la case permet d'en écarter
    /// un mois particulier (un mois sans paie, un mois déjà traité à la main) sans avoir à
    /// refaire deux plages séparées.
    /// </summary>
    public class MoisSelectionnable : INotifyPropertyChanged
    {
        private bool _estSelectionne = true;

        public DateTime Mois { get; }

        public bool EstSelectionne
        {
            get => _estSelectionne;
            set
            {
                if (_estSelectionne != value)
                {
                    _estSelectionne = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>« Janvier 2026 », première lettre capitale, pour l'affichage en case à cocher.</summary>
        public string Libelle
        {
            get
            {
                string libelle = Mois.ToString("MMMM yyyy", new CultureInfo("fr-FR"));
                return libelle.Length > 0
                    ? char.ToUpperInvariant(libelle[0]) + libelle.Substring(1)
                    : libelle;
            }
        }

        public MoisSelectionnable(DateTime mois)
        {
            Mois = new DateTime(mois.Year, mois.Month, 1);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Tous les mois entre deux dates (bornes comprises), du plus ancien au plus
        /// récent. Une borne de fin antérieure à la borne de début ne renvoie qu'un seul
        /// mois — celui du début — plutôt qu'une plage vide ou inversée.
        /// </summary>
        public static System.Collections.Generic.List<MoisSelectionnable> PlageEntre(DateTime debut, DateTime fin)
        {
            var resultat = new System.Collections.Generic.List<MoisSelectionnable>();
            var curseur = new DateTime(debut.Year, debut.Month, 1);
            var borneFin = new DateTime(fin.Year, fin.Month, 1);
            if (borneFin < curseur) borneFin = curseur;

            while (curseur <= borneFin)
            {
                resultat.Add(new MoisSelectionnable(curseur));
                curseur = curseur.AddMonths(1);
            }

            return resultat;
        }
    }
}

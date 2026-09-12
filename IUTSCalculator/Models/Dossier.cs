using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IUTSCalculator.Models
{
    /// <summary>
    /// Représente un dossier (une entreprise/un client) dont les employés et les calculs
    /// sont stockés dans une base de données SQLite qui lui est propre. Cette séparation
    /// physique par dossier garantit qu'aucun employé ne peut être mélangé entre deux
    /// entreprises différentes.
    /// </summary>
    public class Dossier : INotifyPropertyChanged
    {
        private string _raisonSociale;
        private string _sigle;
        private string _ifu;
        private string _rccm;
        private string _regimeFiscal;
        private string _adresse;
        private string _ville;
        private string _telephone;
        private string _logoPath;

        /// <summary>
        /// Identifiant unique du dossier
        /// </summary>
        public string DossierId { get; set; }

        /// <summary>
        /// Nom du fichier de base de données SQLite propre à ce dossier
        /// (chemin relatif au dossier de données de l'application, ex: "Dossiers\dossier_xxx.db")
        /// </summary>
        public string DbFileName { get; set; }

        /// <summary>
        /// Raison sociale de l'entreprise (obligatoire)
        /// </summary>
        public string RaisonSociale
        {
            get => _raisonSociale;
            set
            {
                if (_raisonSociale != value)
                {
                    _raisonSociale = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Sigle ou nom court de l'entreprise
        /// </summary>
        public string Sigle
        {
            get => _sigle;
            set
            {
                if (_sigle != value)
                {
                    _sigle = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Identifiant Fiscal Unique (IFU / NIF)
        /// </summary>
        public string IFU
        {
            get => _ifu;
            set
            {
                if (_ifu != value)
                {
                    _ifu = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Registre du Commerce et du Crédit Mobilier
        /// </summary>
        public string RCCM
        {
            get => _rccm;
            set
            {
                if (_rccm != value)
                {
                    _rccm = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Régime fiscal de l'entreprise (Réel Normal, Réel Simplifié, etc.)
        /// </summary>
        public string RegimeFiscal
        {
            get => _regimeFiscal;
            set
            {
                if (_regimeFiscal != value)
                {
                    _regimeFiscal = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Adresse de l'entreprise
        /// </summary>
        public string Adresse
        {
            get => _adresse;
            set
            {
                if (_adresse != value)
                {
                    _adresse = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Ville de l'entreprise
        /// </summary>
        public string Ville
        {
            get => _ville;
            set
            {
                if (_ville != value)
                {
                    _ville = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Numéro de téléphone de l'entreprise
        /// </summary>
        public string Telephone
        {
            get => _telephone;
            set
            {
                if (_telephone != value)
                {
                    _telephone = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Chemin complet vers le fichier image du logo de l'entreprise (PNG/JPG), utilisé
        /// notamment dans l'en-tête des bulletins de paie. Optionnel.
        /// </summary>
        public string LogoPath
        {
            get => _logoPath;
            set
            {
                if (_logoPath != value)
                {
                    _logoPath = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Date de création du dossier
        /// </summary>
        public DateTime DateCreation { get; set; } = DateTime.Now;

        /// <summary>
        /// Date de dernière ouverture du dossier
        /// </summary>
        public DateTime? DerniereOuverture { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => RaisonSociale;
    }
}

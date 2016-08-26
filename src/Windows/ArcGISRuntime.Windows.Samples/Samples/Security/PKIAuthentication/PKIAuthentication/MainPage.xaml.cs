// Copyright 2016 Esri.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at: http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an 
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific 
// language governing permissions and limitations under the License.

using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;
using System;
using System.Linq;
using System.Text;
using Windows.Security.Cryptography.Certificates;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace PKIAuthentication
{
    public sealed partial class MainPage : Page
    {
        //TODO - Create a client certificate (*.pfx) and add it to a folder accessible to the app

        //TODO - Add the URL for your PKI-secured portal
        const string SecuredPortalUrl = "https://portalpkiqa.ags.esri.com/sharing/rest";

        //TODO - Add the URL for a portal containing public content (ArcGIS Organization, e.g.)
        const string PublicPortalUrl = "http://esrihax.maps.arcgis.com/sharing/rest";

        // Variable to hold certificate information as an encrypted string
        private string _certificateString = string.Empty;

        // Display name to use for the certificate
        private string _certificateName = string.Empty;

        // Variables to point to public and secured portals
        ArcGISPortal _pkiSecuredPortal = null;
        ArcGISPortal _publicPortal = null;

        // Flag variable to track if web map items are from the public or secured portal
        bool _usingPublicPortal;

        public MainPage()
        {
            InitializeComponent();
        }

        private async void ChooseCertificateFile(object sender, RoutedEventArgs e)
        {
            // Create a file picker dialog so the user can select an exported certificate (*.pfx)
            var pfxFilePicker = new FileOpenPicker();
            pfxFilePicker.FileTypeFilter.Add(".pfx");
            pfxFilePicker.CommitButtonText = "Open";

            // Show the dialog and get the selected file (if any)
            StorageFile pfxFile = await pfxFilePicker.PickSingleFileAsync();
            if (pfxFile != null)
            {
                // Use the file's display name for the certificate name
                _certificateName = pfxFile.DisplayName;

                // Read the contents of the file
                IBuffer buffer = await FileIO.ReadBufferAsync(pfxFile);
                using (DataReader dataReader = DataReader.FromBuffer(buffer))
                {
                    // Store the contents of the file as an encrypted string
                    // The string will be imported as a certificate when the user enters the password
                    byte[] bytes = new byte[buffer.Length];
                    dataReader.ReadBytes(bytes);
                    _certificateString = Convert.ToBase64String(bytes);
                }

                // Show the certificate password box (and hide the map search controls)
                LoginPanel.Visibility = Visibility.Visible;
                LoadMapPanel.Visibility = Visibility.Collapsed;
            }
        }

        // Load a client certificate for accessing a PKI-secured server 
        private async void LoadClientCertButton_Click(object sender, RoutedEventArgs e)
        {
            // Show the progress bar and a message
            ProgressStatus.Visibility = Visibility.Visible;
            MessagesTextBlock.Text = "Loading certificate ...";

            try
            {                
                // Import the certificate by providing: 
                // -the encoded certificate string, 
                // -the password (entered by the user)
                // -certificate options (export, key protection, install)
                // -a friendly name (the name of the pfx file)
                await CertificateEnrollmentManager.ImportPfxDataAsync(
                    _certificateString,
                    CertPasswordBox.Password,
                    ExportOption.Exportable,
                    KeyProtectionLevel.NoConsent,
                    InstallOptions.None,
                    _certificateName);

                // Report success
                MessagesTextBlock.Text = "Client certificate (" + _certificateName + ") was successfully imported";
            }
            catch (Exception ex)
            {
                // Report error
                MessagesTextBlock.Text = "Error loading certificate: " + ex.Message;
            }
            finally
            {
                // Hide progress bar and the password controls
                ProgressStatus.Visibility = Visibility.Collapsed;
                HideCertLogin(null, null);
            }
        }

        // Search the public portal for web maps and display the results in a list box.
        private async void SearchPublicMapsButton_Click(object sender, RoutedEventArgs e)
        {
            // Set the flag variable to indicate the search is from the public portal
            // (if the user wants to load a map, will need the portal it came from)
            _usingPublicPortal = true;

            MapItemListBox.Items.Clear();

            // Show status message and the status bar
            MessagesTextBlock.Text = "Searching for web map items on the public portal.";
            ProgressStatus.Visibility = Visibility.Visible;
            var messageBuilder = new StringBuilder();

            try
            {
                // Create an instance of the public portal
                _publicPortal = await ArcGISPortal.CreateAsync(new Uri(PublicPortalUrl));

                // Report a successful connection
                messageBuilder.AppendLine("Connected to the portal on " + _publicPortal.Uri.Host);
                messageBuilder.AppendLine("Version: " + _publicPortal.CurrentVersion);

                // Report the user name used for this connection
                if (_publicPortal.CurrentUser != null)
                {
                    messageBuilder.AppendLine("Connected as: " + _publicPortal.CurrentUser.UserName);
                }
                else
                {
                    // Connected anonymously
                    messageBuilder.AppendLine("Anonymous");
                }

                // Search the public portal for web maps
                var items = await _publicPortal.SearchItemsAsync(new SearchParameters("type:(\"web map\" NOT \"web mapping application\")"));

                // Build a list of items from the results that shows the map name and stores the item ID (with the Tag property)
                var resultItems = from r in items.Results select new ListBoxItem { Tag = r.Id, Content = r.Title };

                // Add the list items
                foreach (var itm in resultItems)
                {
                    MapItemListBox.Items.Add(itm);
                }
            }
            catch (Exception ex)
            {
                // Report errors connecting to or searching the public portal
                messageBuilder.AppendLine(ex.Message);
            }
            finally
            {
                // Show messages, hide progress bar
                MessagesTextBlock.Text = messageBuilder.ToString();
                ProgressStatus.Visibility = Visibility.Collapsed;
            }
        }

        // Search the PKI-secured portal for web maps and display the results in a list box.
        private async void SearchSecureMapsButton_Click(object sender, RoutedEventArgs e)
        {
            // Set the flag variable to indicate the search is on the secure portal
            // (if the user wants to load a map, will need the portal it came from)
            _usingPublicPortal = false;

            MapItemListBox.Items.Clear();

            // Show status message and the status bar
            MessagesTextBlock.Text = "Searching for web map items on the secure portal.";
            ProgressStatus.Visibility = Visibility.Visible;
            var messageBuilder = new StringBuilder();

            try
            {
                // Create an instance of the PKI-secured portal
                // Note: if the "Private Networks" capability is not added to package.appxmanifest, you may get an exception here
                _pkiSecuredPortal = await ArcGISPortal.CreateAsync(new Uri(SecuredPortalUrl));

                // Report a successful connection
                messageBuilder.AppendLine("Connected to the portal on " + _pkiSecuredPortal.Uri.Host);
                messageBuilder.AppendLine("Version: " + _pkiSecuredPortal.CurrentVersion);

                // Report the user name used for this connection
                if (_pkiSecuredPortal.CurrentUser != null)
                {
                    messageBuilder.AppendLine("Connected as: " + _pkiSecuredPortal.CurrentUser.UserName);
                }
                else
                {
                    // (this shouldn't happen)
                    messageBuilder.AppendLine("Anonymous");
                }

                // Search the secured portal for web maps
                var items = await _pkiSecuredPortal.SearchItemsAsync(new SearchParameters("type:(\"web map\" NOT \"web mapping application\")"));

                // Build a list of items from the results that shows the map name and stores the item ID (with the Tag property)
                var resultItems = from r in items.Results select new ListBoxItem { Tag = r.Id, Content = r.Title };

                // Add the list items
                foreach (var itm in resultItems)
                {
                    MapItemListBox.Items.Add(itm);
                }
            }
            catch (Exception ex)
            {
                // Report errors connecting to or searching the secured portal
                messageBuilder.AppendLine(ex.Message);
                if (ex.InnerException != null)
                {
                    messageBuilder.AppendLine("--" + ex.InnerException.Message);
                }
            }
            finally
            {
                // Show messages, hide progress bar
                MessagesTextBlock.Text = messageBuilder.ToString();
                ProgressStatus.Visibility = Visibility.Collapsed;
            }
        }

        private async void AddMapItem_Click(object sender, RoutedEventArgs e)
        {
            // Get a web map from the selected portal item and display it in the app
            if (MapItemListBox.SelectedItem == null) { return; }

            // Clear status messages
            MessagesTextBlock.Text = string.Empty;
            var messageBuilder = new StringBuilder();

            try
            {
                // See if we're using the public or secured portal, then get the appropriate object reference
                ArcGISPortal portal = null;
                if (_usingPublicPortal)
                {
                    portal = _publicPortal;
                }
                else
                {
                    portal = _pkiSecuredPortal;
                }

                // Throw an exception if the portal is null
                if (portal == null)
                {
                    throw new Exception("Portal has not been instantiated.");
                }

                // Get the portal item ID from the selected list box item (read it from the Tag property)
                var itemId = (MapItemListBox.SelectedItem as ListBoxItem).Tag.ToString();

                // Use the item ID to create an ArcGISPortalItem from the appropriate portal 
                var portalItem = await ArcGISPortalItem.CreateAsync(portal, itemId);

                // Create a WebMap from the portal item (all items in the list represent web maps)
                var webMap = new Map(portalItem);

                // Display the Map in the map view
                MyMapView.Map = webMap;

                // Report success
                messageBuilder.AppendLine("Successfully loaded web map from item #" + itemId + " from " + portal.Uri.Host);
            }
            catch (Exception ex)
            {
                // Add an error message
                messageBuilder.AppendLine("Error accessing web map: " + ex.Message);
            }
            finally
            {
                // Show messages
                MessagesTextBlock.Text = messageBuilder.ToString();
            }
        }

        private void HideCertLogin(object sender, RoutedEventArgs e)
        {
            // Hide the certificate password box (and show the map search controls)
            LoginPanel.Visibility = Visibility.Collapsed;
            LoadMapPanel.Visibility = Visibility.Visible;
        }
    }
}

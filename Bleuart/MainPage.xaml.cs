using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.Collections.ObjectModel;
using BLE;
using Windows.UI.Core;
using System.Text;
using Windows.Security.Cryptography;
using Windows.System;

namespace Bleuart
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private ObservableCollection<BluetoothLEDeviceDisplay> devices = new ObservableCollection<BluetoothLEDeviceDisplay>();
        private ObservableCollection<BluetoothLEAttributeDisplay> services = new ObservableCollection<BluetoothLEAttributeDisplay>();
        private ObservableCollection<BluetoothLEAttributeDisplay> characteristics = new ObservableCollection<BluetoothLEAttributeDisplay>();
        private DeviceWatcher deviceWatcher;
        private BluetoothLEDevice bleDevice = null;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void btnFindDevice_Click(object sender, RoutedEventArgs e)
        {
            if (deviceWatcher == null)
            {
                StartBleDeviceWatcher();
            }
            else
            {
                StopBleDeviceWatcher();
            }

        }

        private void StopBleDeviceWatcher()
        {
            if (deviceWatcher != null)
            {
                // Unregister the event handlers.
                deviceWatcher.Added -= DeviceWatcher_Added;
                deviceWatcher.Updated -= DeviceWatcher_Updated;
                deviceWatcher.Removed -= DeviceWatcher_Removed;
                deviceWatcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted;
 //               deviceWatcher.Stopped -= DeviceWatcher_Stopped;

                // Stop the watcher.
                deviceWatcher.Stop();
                deviceWatcher = null;
            }
        }

        /// <summary>
        ///     Starts a device watcher that looks for all nearby BT devices (paired or unpaired). Attaches event handlers and
        ///     populates the collection of devices.
        /// </summary>
        private void StartBleDeviceWatcher()
        {
            // Additional properties we would like about the device.
            string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };

            // BT_Code: Currently Bluetooth APIs don't provide a selector to get ALL devices that are both paired and non-paired.
            deviceWatcher =
                    DeviceInformation.CreateWatcher(
                        "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")",
                        requestedProperties,
                        DeviceInformationKind.AssociationEndpoint);

            // Register event handlers before starting the watcher.
            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Updated += DeviceWatcher_Updated;
            deviceWatcher.Removed += DeviceWatcher_Removed;
            deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
//            deviceWatcher.Stopped += DeviceWatcher_Stopped;

            // Start over with an empty collection.
            devices.Clear();

            // Start the watcher.
            deviceWatcher.Start();
        }

        #region DeviceWatcher_Events
        private BluetoothLEDeviceDisplay FindBluetoothLEDeviceDisplay(string id)
        {
            return devices.FirstOrDefault(d => d.Id == id);
        }

        private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation deviceInfo)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Protect against race condition if the task runs after the app stopped the deviceWatcher.
                if (sender == deviceWatcher)
                {
                    // Make sure device name isn't blank or already present in the list.
                    if (deviceInfo.Name != string.Empty && FindBluetoothLEDeviceDisplay(deviceInfo.Id) == null)
                    {
                        devices.Add(new BluetoothLEDeviceDisplay(deviceInfo));
                    }
                }
            });
        }

        private async void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Protect against race condition if the task runs after the app stopped the deviceWatcher.
                if (sender == deviceWatcher)
                {
                    BluetoothLEDeviceDisplay bleDeviceDisplay = FindBluetoothLEDeviceDisplay(deviceInfoUpdate.Id);
                    if (bleDeviceDisplay != null)
                    {
                        bleDeviceDisplay.Update(deviceInfoUpdate);
                    }
                }
            });
        }

        private async void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Protect against race condition if the task runs after the app stopped the deviceWatcher.
                if (sender == deviceWatcher)
                {
                    // Find the corresponding DeviceInformation in the collection and remove it.
                    BluetoothLEDeviceDisplay bleDeviceDisplay = FindBluetoothLEDeviceDisplay(deviceInfoUpdate.Id);
                    if (bleDeviceDisplay != null)
                    {
                        devices.Remove(bleDeviceDisplay);
                    }
                }
            });
        }

        private async void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object e)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Protect against race condition if the task runs after the app stopped the deviceWatcher.
                if (sender == deviceWatcher)
                {
                    cbDevices.PlaceholderText = $"{devices.Count} devices";
                    deviceWatcher = null;
                }
            });
        }

        #endregion DeviceWatcher_Events

        private async void cbDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bleDevice?.Dispose();
            bleDevice = null;

            var bleDeviceDisp = cbDevices.SelectedItem as BluetoothLEDeviceDisplay;
            if (bleDeviceDisp == null) return;

            services.Clear();
            try
            {
                bleDevice = await BluetoothLEDevice.FromIdAsync(bleDeviceDisp.Id);
            }
            catch (Exception ex) when ((uint)ex.HResult == 0x800710df)
            {
                // ERROR_DEVICE_NOT_AVAILABLE because the Bluetooth radio is not on.
            }

            if (bleDevice != null)
            {
                foreach (var service in bleDevice.GattServices)
                {
                    services.Add(new BluetoothLEAttributeDisplay(service));
                }
            }
        }

        private void cbDevices_DropDownOpened(object sender, object e)
        {
            if (deviceWatcher == null)
            {
                StartBleDeviceWatcher();
            }
        }

        private void cbServices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var attrInfoDisp = (BluetoothLEAttributeDisplay)cbServices.SelectedItem;
            if (attrInfoDisp == null) return;

            characteristics.Clear();
            IReadOnlyList<GattCharacteristic> chars = null;
            try
            {
                chars = attrInfoDisp.service.GetAllCharacteristics();
            }
            catch (Exception ex)
            {
                chars = new List<GattCharacteristic>();
            }

            foreach (GattCharacteristic c in chars)
            {
                characteristics.Add(new BluetoothLEAttributeDisplay(c));
            }
        }

        private async void cbCharacteristics_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Set up notification
            try
            {
                var attrinfo = cbCharacteristics.SelectedItem as BluetoothLEAttributeDisplay;
                if (attrinfo == null) return;
                var characteristic = attrinfo.characteristic;

                // BT_Code: Must write the CCCD in order for server to send notifications.
                // We receive them in the ValueChanged event handler.
                // Note that this sample configures either Indicate or Notify, but not both.
                var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.Notify);
                if (result == GattCommunicationStatus.Success)
                {
                    characteristic.ValueChanged += Characteristic_ValueChanged;
                }
            }
            catch (Exception ex) when ((uint)ex.HResult == 0x80650003)
            {
                // This happens if you picked a characteristic without a Notify

            }
            catch (UnauthorizedAccessException)
            {
                // This usually happens when a device reports that it support notify, but it actually doesn't.
            }
        }

        private async void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var buffer = args.CharacteristicValue;
            string text;
            byte[] data;
            CryptographicBuffer.CopyToByteArray(buffer, out data);
            text = Encoding.UTF8.GetString(data);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => tbResponse.Text += text);
        }

        private async void tbTerminal_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            if (tbTerminal.Text == "") return;

            int i;
            for (i=0; i<tbTerminal.Text.Length; i++)
            {
                string text = tbTerminal.Text.Substring(i, 1);
                var data = CryptographicBuffer.ConvertStringToBinary(text, BinaryStringEncoding.Utf8);

                var attrinfo = cbCharacteristics.SelectedItem as BluetoothLEAttributeDisplay;
                if (attrinfo == null) return;
                var characteristic = attrinfo.characteristic;

                try
                {
                    var result = await characteristic.WriteValueAsync(data);
                    if (result == GattCommunicationStatus.Success)
                    {
                        tbResponse.Text += text;
                    }
                }
                catch (Exception ex) when ((uint)ex.HResult == 0x80650003 || (uint)ex.HResult == 0x80070005)
                {
                }
            }
            tbTerminal.Text = "";
        }

    }
}

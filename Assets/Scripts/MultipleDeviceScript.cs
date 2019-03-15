﻿/* This is an example to show how to connect to 2 HM-10 devices
 * that are connected together via their serial pins and send data
 * back and forth between them.
 */

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Text;

public class MultipleDeviceScript : MonoBehaviour
{
	public string DeviceName = "DSD TECH";
	public string ServiceUUID = "FFE0";
	public string Characteristic = "FFE1";

	public Text HM10_Status;
	public Text BluetoothStatus;
	public GameObject PanelMiddle;
	public Text TextToSend;

	enum States
	{
		None,
		Scan,
		Connect,
		Subscribe,
		Unsubscribe,
		Disconnect,
		Communication,
	}

	private bool _workingFoundDevice = true;
	private bool _connected = false;
	private float _timeout = 0f;
	private States _state = States.None;
	private bool _foundID = false;

	private List<string> _hm10;
	private int _hm10CurrentIndex;

	public void OnButton (Button button)
	{
		if (button.name.Contains ("Send"))
		{
			if (string.IsNullOrEmpty (TextToSend.text))
			{
				BluetoothStatus.text = "Enter text to send...";
			}
			else
			{
				SendString (TextToSend.text, 0);
			}
		}
	}

	void Reset ()
	{
		_workingFoundDevice = false;    // used to guard against trying to connect to a second device while still connecting to the first
		_connected = false;
		_timeout = 0f;
		_state = States.None;
		_foundID = false;
		_hm10 = new List<string> ();
		_hm10CurrentIndex = 0;
		PanelMiddle.SetActive (false);
	}

	void SetState (States newState, float timeout)
	{
		_state = newState;
		_timeout = timeout;
	}

	void StartProcess ()
	{
		BluetoothStatus.text = "Initializing...";

		Reset ();
		BluetoothLEHardwareInterface.Initialize (true, false, () => {

			SetState (States.Scan, 0.1f);
			BluetoothStatus.text = "Initialized";

		}, (error) => {

			BluetoothLEHardwareInterface.Log ("Error: " + error);
		});
	}

	// Use this for initialization
	void Start ()
	{
		HM10_Status.text = "";

		StartProcess ();
	}

	// Update is called once per frame
	void Update ()
	{
		if (_timeout > 0f)
		{
			_timeout -= Time.deltaTime;
			if (_timeout <= 0f)
			{
				_timeout = 0f;

				switch (_state)
				{
				case States.None:
					break;

				case States.Scan:
					BluetoothStatus.text = "Scanning for HM10 devices...";

					BluetoothLEHardwareInterface.ScanForPeripheralsWithServices (null, (address, name) => {

						// we only want to look at devices that have the name we are looking for
						// this is the best way to filter out devices
						if (!_workingFoundDevice && name.Contains (DeviceName))
						{
							_workingFoundDevice = true;

							// it is always a good idea to stop scanning while you connect to a device
							// and get things set up
							BluetoothLEHardwareInterface.StopScan ();
							BluetoothStatus.text = "";

							// add it to the list and set to connect to it
							_hm10CurrentIndex = _hm10.Count;
							_hm10.Add (address);

							BluetoothStatus.text = "Found HM10";

							SetState (States.Connect, 0.5f);
						}

					}, null, false, false);
					break;

				case States.Connect:
					// set these flags
					_foundID = false;

					BluetoothStatus.text = "Connecting to HM10";

					// note that the first parameter is the address, not the name. I have not fixed this because
					// of backwards compatiblity.
					// also note that I am note using the first 2 callbacks. If you are not looking for specific characteristics you can use one of
					// the first 2, but keep in mind that the device will enumerate everything and so you will want to have a timeout
					// large enough that it will be finished enumerating before you try to subscribe or do any other operations.
					BluetoothLEHardwareInterface.ConnectToPeripheral (_hm10[_hm10CurrentIndex], null, null, (address, serviceUUID, characteristicUUID) => {

						if (IsEqual (serviceUUID, ServiceUUID))
						{
							// if we have found the characteristic that we are waiting for
							// set the state. make sure there is enough timeout that if the
							// device is still enumerating other characteristics it finishes
							// before we try to subscribe
							if (IsEqual (characteristicUUID, Characteristic))
							{
								_connected = true;
								SetState (States.Subscribe, 2f);

								BluetoothStatus.text = "Connected to HM10";
							}
						}
					}, (disconnectedAddress) => {
						BluetoothLEHardwareInterface.Log ("Device disconnected: " + disconnectedAddress);
						BluetoothStatus.text = "Disconnected";
					});
					break;

				case States.Subscribe:
					BluetoothStatus.text = "Subscribing to HM10";

					BluetoothLEHardwareInterface.SubscribeCharacteristicWithDeviceAddress (_hm10[_hm10CurrentIndex], ServiceUUID, Characteristic, null, (address, characteristicUUID, bytes) => {

						HM10_Status.text = "Received Serial: " + Encoding.UTF8.GetString (bytes);
					});

					// set to the none state and the user can start sending and receiving data
					_state = States.None;
					BluetoothStatus.text = "Waiting...";

					if (_hm10CurrentIndex == 1)
						PanelMiddle.SetActive (true);
					else
					{
						_workingFoundDevice = false;
						SetState (States.Scan, 3f);
					}
					break;

				case States.Unsubscribe:
					BluetoothLEHardwareInterface.UnSubscribeCharacteristic (_hm10[_hm10CurrentIndex], ServiceUUID, Characteristic, null);
					SetState (States.Disconnect, 4f);
					break;

				case States.Disconnect:
					if (_connected)
					{
						BluetoothLEHardwareInterface.DisconnectPeripheral (_hm10[_hm10CurrentIndex], (address) => {
							BluetoothLEHardwareInterface.DeInitialize (() => {

								_connected = false;
								_state = States.None;
							});
						});
					}
					else
					{
						BluetoothLEHardwareInterface.DeInitialize (() => {

							_state = States.None;
						});
					}
					break;
				}
			}
		}
	}

	string FullUUID (string uuid)
	{
		return "0000" + uuid + "-0000-1000-8000-00805F9B34FB";
	}

	bool IsEqual (string uuid1, string uuid2)
	{
		if (uuid1.Length == 4)
			uuid1 = FullUUID (uuid1);
		if (uuid2.Length == 4)
			uuid2 = FullUUID (uuid2);

		return (uuid1.ToUpper ().Equals (uuid2.ToUpper ()));
	}

	void SendString (string value, int hm10Index)
	{
		var data = Encoding.UTF8.GetBytes (value);
		// notice that the 6th parameter is false. this is because the HM10 doesn't support withResponse writing to its characteristic.
		// some devices do support this setting and it is prefered when they do so that you can know for sure the data was received by 
		// the device
		BluetoothLEHardwareInterface.WriteCharacteristic (_hm10[hm10Index], ServiceUUID, Characteristic, data, data.Length, false, (characteristicUUID) => {

			BluetoothLEHardwareInterface.Log ("Write Succeeded");
		});
	}

	void SendByte (byte value, int hm10Index)
	{
		byte[] data = new byte[] { value };
		// notice that the 6th parameter is false. this is because the HM10 doesn't support withResponse writing to its characteristic.
		// some devices do support this setting and it is prefered when they do so that you can know for sure the data was received by 
		// the device
		BluetoothLEHardwareInterface.WriteCharacteristic (_hm10[hm10Index], ServiceUUID, Characteristic, data, data.Length, false, (characteristicUUID) => {

			BluetoothLEHardwareInterface.Log ("Write Succeeded");
		});
	}
}

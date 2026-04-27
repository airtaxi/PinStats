// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;
using DiskInfoToolkitSmartAttributeEntry = DiskInfoToolkit.SmartAttributeEntry;
using DiskInfoToolkitStorageDevice = DiskInfoToolkit.StorageDevice;

namespace LibreHardwareMonitor.Hardware.Storage;

public sealed class StorageDevice : Hardware, ISmart
{
    private const double BytesPerGigabyte = 1024.0 * 1024.0 * 1024.0;
    private const byte NvmeTemperatureSensorAttributeBaseIdentifier = 0xF0;

    private readonly PerformanceValue _performanceRead = new();
    private readonly PerformanceValue _performanceTotal = new();
    private readonly PerformanceValue _performanceWrite = new();
    private readonly List<StorageDeviceSensor> _sensors = new();
    private readonly List<SmartAttribute> _attributes = new();

    private DiskInfoToolkitStorageDevice _storageDevice;
    private long _lastReadCount;
    private long _lastTime;
    private long _lastWriteCount;
    private DateTime _lastUpdate = DateTime.MinValue;

    private Sensor _sensorDiskReadActivity;
    private Sensor _sensorDiskReadRate;
    private Sensor _sensorDiskTotalActivity;
    private Sensor _sensorDiskWriteActivity;
    private Sensor _sensorDiskWriteRate;
    private Sensor _usageSensor;
    private Sensor _freeSpaceSensor;
    private Sensor _totalSpaceSensor;

    public StorageDevice(DiskInfoToolkitStorageDevice storageDevice, string identifierPrefix, string identifierValue, ISettings settings)
        : base(GetDisplayName(storageDevice), new Identifier(identifierPrefix, identifierValue), settings)
    {
        _storageDevice = storageDevice;

        CreateAttributes();

        CreateSensors();
    }

    public override HardwareType HardwareType => HardwareType.Storage;

    public DiskInfoToolkitStorageDevice Storage => _storageDevice;

    public IReadOnlyList<SmartAttribute> Attributes => _attributes;

    public static TimeSpan ThrottleInterval { get; set; }

    public bool Matches(DiskInfoToolkitStorageDevice storageDevice)
    {
        if (storageDevice == null) return false;
        if (_storageDevice == storageDevice) return true;
        if (_storageDevice.StorageDeviceNumber.HasValue && storageDevice.StorageDeviceNumber.HasValue && _storageDevice.StorageDeviceNumber.Value == storageDevice.StorageDeviceNumber.Value) return true;
        if (!string.IsNullOrWhiteSpace(_storageDevice.DeviceInstanceID) && string.Equals(_storageDevice.DeviceInstanceID, storageDevice.DeviceInstanceID, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(_storageDevice.DevicePath) && string.Equals(_storageDevice.DevicePath, storageDevice.DevicePath, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(_storageDevice.AlternateDevicePath) && string.Equals(_storageDevice.AlternateDevicePath, storageDevice.AlternateDevicePath, StringComparison.OrdinalIgnoreCase)) return true;

        return !string.IsNullOrWhiteSpace(_storageDevice.SerialNumber)
            && string.Equals(_storageDevice.SerialNumber, storageDevice.SerialNumber, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_storageDevice.ProductName ?? string.Empty, storageDevice.ProductName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    public void Refresh(DiskInfoToolkitStorageDevice storageDevice)
    {
        if (storageDevice == null) return;

        _storageDevice = storageDevice;
        UpdateAttributes();
        UpdateSpaceSensors();
    }

    public override void Update()
    {
        if (DateTime.UtcNow - _lastUpdate < ThrottleInterval) return;

        _lastUpdate = DateTime.UtcNow;

        ToggleSpaceSensors();
        UpdatePerformanceSensors();
        DiskInfoToolkit.Storage.Refresh(_storageDevice);
        UpdateSpaceSensors();
        UpdateAttributes();

        _sensors.ForEach(storageDeviceSensor => storageDeviceSensor.Update(_storageDevice));
    }

    public override string GetReport()
    {
        var reportBuilder = new StringBuilder();
        reportBuilder.AppendLine("Storage");
        reportBuilder.AppendLine();
        reportBuilder.AppendLine($"Drive Name: {GetDisplayName(_storageDevice)}");
        reportBuilder.AppendLine($"Product Revision: {_storageDevice.ProductRevision}");
        reportBuilder.AppendLine($"Serial Number: {_storageDevice.SerialNumber}");
        reportBuilder.AppendLine($"Device Path: {_storageDevice.DevicePath}");
        reportBuilder.AppendLine();
        reportBuilder.AppendLine("Smart Attributes:");
        reportBuilder.AppendLine("ID, Description, Value, Threshold");

        foreach (var attribute in _attributes)
        {
            reportBuilder.AppendLine($"{attribute.Id,3}, {attribute.Name,60}, {attribute.Value,18}, {attribute.Threshold,3}");
        }

        reportBuilder.AppendLine();

        if (!_storageDevice.IsDynamicDisk)
        {
            reportBuilder.AppendLine("Partitions:");

            foreach (var partition in _storageDevice.Partitions)
            {
                reportBuilder.AppendLine($"Partition #{partition.PartitionNumber}");

                if (partition.DriveLetter != null) reportBuilder.AppendLine($"Drive Letter: {partition.DriveLetter}");
                if (partition.AvailableFreeSpaceBytes != null) reportBuilder.AppendLine($"Available Free Space: {partition.AvailableFreeSpaceBytes}");
            }

            reportBuilder.AppendLine();

            if (_storageDevice.TotalPartitionFreeSpaceBytes != null) reportBuilder.AppendLine($"Total Free Size: {_storageDevice.TotalPartitionFreeSpaceBytes}");
        }

        reportBuilder.AppendLine($"Total Size: {_storageDevice.DiskSizeBytes}");

        return reportBuilder.ToString();
    }

    public override void Traverse(IVisitor visitor)
    {
        foreach (ISensor sensor in Sensors)
            sensor.Accept(visitor);
    }

    private void CreateAttributes()
    {
        _attributes.Clear();

        var attributes = SmartAttributeTranslator.GetAttributesFor(_storageDevice);

        _attributes.AddRange(attributes.Where(attribute => attribute != null));
    }

    private void UpdateAttributes()
    {
        if (_storageDevice.SmartAttributes == null) return;

        foreach (var smartAttribute in _storageDevice.SmartAttributes)
        {
            var foundAttribute = _attributes.Find(attribute => attribute.Matches(smartAttribute));

            if (foundAttribute != null) foundAttribute.Attribute = smartAttribute;
        }
    }

    private void CreateSensors()
    {
        if (IsNvmeStorageDevice())
        {
            AddSensor("Composite Temperature", 0, false, SensorType.Temperature, storageDevice => storageDevice.Temperature.GetValueOrDefault());

            TryAddTemperatureSensor(1, false, 1);
            TryAddTemperatureSensor(2, false, 2);
            TryAddTemperatureSensor(3, false, 3);
            TryAddTemperatureSensor(4, false, 4);
            TryAddTemperatureSensor(5, false, 5);
            TryAddTemperatureSensor(6, false, 6);
            TryAddTemperatureSensor(7, false, 7);
            TryAddTemperatureSensor(8, false, 8);

            AddSensor("Warning Temperature", 10, false, SensorType.Temperature, storageDevice => storageDevice.TemperatureWarning.GetValueOrDefault());
            AddSensor("Critical Temperature", 11, false, SensorType.Temperature, storageDevice => storageDevice.TemperatureCritical.GetValueOrDefault());
        }
        else
        {
            AddSensor("Temperature", 0, false, SensorType.Temperature, storageDevice => storageDevice.Temperature.GetValueOrDefault());
        }

        if (_storageDevice.Health.HasValue) AddSensor("Life", 20, false, SensorType.Level, storageDevice => storageDevice.Health.GetValueOrDefault());
        if (_storageDevice.HostReads.HasValue) AddSensor("Data Read", 21, false, SensorType.Data, storageDevice => storageDevice.HostReads.GetValueOrDefault());
        if (_storageDevice.HostWrites.HasValue) AddSensor("Data Written", 22, false, SensorType.Data, storageDevice => storageDevice.HostWrites.GetValueOrDefault());
        if (_storageDevice.PowerOnCount.HasValue) AddSensor("Power On Count", 23, false, SensorType.Factor, storageDevice => storageDevice.PowerOnCount.GetValueOrDefault());
        if (_storageDevice.PowerOnHours.HasValue) AddSensor("Power On Hours", 24, false, SensorType.Factor, storageDevice => storageDevice.PowerOnHours.GetValueOrDefault());

        _usageSensor = new Sensor("Used Space", 30, SensorType.Load, this, _settings);
        _freeSpaceSensor = new Sensor("Free Space", 31, SensorType.Data, this, _settings);
        ToggleSpaceSensors();

        _totalSpaceSensor = new Sensor("Total Space", 32, SensorType.Data, this, _settings);
        ActivateSensor(_totalSpaceSensor);
        UpdateSpaceSensors();

        _sensorDiskReadActivity = new Sensor("Read Activity", 51, SensorType.Load, this, _settings);
        ActivateSensor(_sensorDiskReadActivity);

        _sensorDiskWriteActivity = new Sensor("Write Activity", 52, SensorType.Load, this, _settings);
        ActivateSensor(_sensorDiskWriteActivity);

        _sensorDiskTotalActivity = new Sensor("Total Activity", 53, SensorType.Load, this, _settings);
        ActivateSensor(_sensorDiskTotalActivity);

        _sensorDiskReadRate = new Sensor("Read Rate", 54, SensorType.Throughput, this, _settings);
        ActivateSensor(_sensorDiskReadRate);

        _sensorDiskWriteRate = new Sensor("Write Rate", 55, SensorType.Throughput, this, _settings);
        ActivateSensor(_sensorDiskWriteRate);

        AddSmartAttributeSensors();
    }

    private void TryAddTemperatureSensor(int sensorIndex, bool defaultHidden, int thermalSensorIndex)
    {
        var smartAttribute = GetSmartAttribute(GetNvmeTemperatureSensorAttributeIdentifier(thermalSensorIndex));
        if (smartAttribute != null && smartAttribute.RawValue > 0)
        {
            AddSensor($"Temperature #{thermalSensorIndex}", sensorIndex, defaultHidden, SensorType.Temperature, storageDevice =>
            {
                var currentSmartAttribute = GetSmartAttribute(GetNvmeTemperatureSensorAttributeIdentifier(thermalSensorIndex));
                if (currentSmartAttribute != null) return ConvertKelvinToCelsius(currentSmartAttribute.RawValue);

                return 0;
            });
        }
    }

    private DiskInfoToolkitSmartAttributeEntry GetSmartAttribute(byte smartAttributeIdentifier) => _storageDevice.SmartAttributes.FirstOrDefault(smartAttribute => smartAttribute.ID == smartAttributeIdentifier);

    private void AddSensor(string name, int index, bool defaultHidden, SensorType sensorType, GetStorageDeviceSensorValue getValue)
    {
        var sensor = new StorageDeviceSensor(name, index, defaultHidden, sensorType, this, _settings, getValue)
        {
            Value = 0
        };

        ActivateSensor(sensor);
        _sensors.Add(sensor);
    }

    private unsafe void UpdatePerformanceSensors()
    {
        var performanceDevicePath = GetPerformanceDevicePath();
        if (string.IsNullOrWhiteSpace(performanceDevicePath)) return;

        DISK_PERFORMANCE diskPerformance = new();

        using var handle = PInvoke.CreateFile(performanceDevicePath,
                                              (uint)FileAccess.ReadWrite,
                                              FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                                              null,
                                              FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                                              FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
                                              null);

        if (handle.IsInvalid) return;

        uint bytesReturned;
        if (!PInvoke.DeviceIoControl((HANDLE)handle.DangerousGetHandle(), PInvoke.IOCTL_DISK_PERFORMANCE, null, 0, &diskPerformance, (uint)sizeof(DISK_PERFORMANCE), &bytesReturned, null)) return;

        _performanceRead.Update(diskPerformance.ReadTime, diskPerformance.QueryTime);
        _sensorDiskReadActivity.Value = (float)_performanceRead.Result;

        _performanceWrite.Update(diskPerformance.WriteTime, diskPerformance.QueryTime);
        _sensorDiskWriteActivity.Value = (float)_performanceWrite.Result;

        _performanceTotal.Update(diskPerformance.IdleTime, diskPerformance.QueryTime);
        _sensorDiskTotalActivity.Value = (float)(100 - _performanceTotal.Result);

        long readCount = diskPerformance.BytesRead;
        long readDifference = readCount - _lastReadCount;
        _lastReadCount = readCount;

        long writeCount = diskPerformance.BytesWritten;
        long writeDifference = writeCount - _lastWriteCount;
        _lastWriteCount = writeCount;

        long currentTime = Stopwatch.GetTimestamp();
        if (_lastTime != 0 && currentTime != _lastTime)
        {
            double timeDeltaSeconds = (currentTime - _lastTime) / (double)Stopwatch.Frequency;

            double writeSpeed = writeDifference * (1 / timeDeltaSeconds);
            _sensorDiskWriteRate.Value = (float)writeSpeed;

            double readSpeed = readDifference * (1 / timeDeltaSeconds);
            _sensorDiskReadRate.Value = (float)readSpeed;
        }

        _lastTime = currentTime;
    }

    private void ToggleSpaceSensors()
    {
        if (!_storageDevice.IsDynamicDisk && !_storageDevice.IsOtherOperatingSystemDisk)
        {
            ActivateSensor(_usageSensor);
            ActivateSensor(_freeSpaceSensor);
        }
        else
        {
            DeactivateSensor(_usageSensor);
            DeactivateSensor(_freeSpaceSensor);
        }
    }

    private void UpdateSpaceSensors()
    {
        var totalSizeBytes = _storageDevice.DiskSizeBytes.GetValueOrDefault();
        var totalFreeSpaceBytes = _storageDevice.TotalPartitionFreeSpaceBytes;

        _totalSpaceSensor.Value = totalSizeBytes > 0 ? (float)ConvertBytesToGigabytes(totalSizeBytes) : null;

        if (totalSizeBytes > 0 && totalFreeSpaceBytes.HasValue)
        {
            _usageSensor.Value = 100.0f - (100.0f * totalFreeSpaceBytes.Value / totalSizeBytes);
            _freeSpaceSensor.Value = (float)ConvertBytesToGigabytes(totalFreeSpaceBytes.Value);
        }
        else
        {
            _usageSensor.Value = null;
            _freeSpaceSensor.Value = null;
        }
    }

    private void AddSmartAttributeSensors()
    {
        var attributes = Attributes.Where(smartAttribute => smartAttribute.SensorType.HasValue)
                                   .GroupBy(smartAttribute => new { smartAttribute.SensorType.Value, smartAttribute.SensorChannel })
                                   .Select(smartAttributeGroup => smartAttributeGroup.First());

        foreach (var smartAttribute in attributes)
        {
            AddSensor(smartAttribute.SensorName,
                      smartAttribute.SensorChannel,
                      smartAttribute.IsHiddenByDefault,
                      smartAttribute.SensorType.Value,
                      storageDevice => smartAttribute.Value);
        }
    }

    private bool IsNvmeStorageDevice()
    {
        return _storageDevice.TransportKind == DiskInfoToolkit.StorageTransportKind.Nvme
            || _storageDevice.BusType == DiskInfoToolkit.StorageBusType.Nvme
            || _storageDevice.SmartAttributeProfile == DiskInfoToolkit.SmartAttributeProfile.NVMe;
    }

    private string GetPerformanceDevicePath()
    {
        if (_storageDevice.StorageDeviceNumber.HasValue) return $@"\\.\PhysicalDrive{_storageDevice.StorageDeviceNumber.Value.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(_storageDevice.AlternateDevicePath)) return _storageDevice.AlternateDevicePath;

        return _storageDevice.DevicePath;
    }

    private static string GetDisplayName(DiskInfoToolkitStorageDevice storageDevice)
    {
        if (!string.IsNullOrWhiteSpace(storageDevice.DisplayName)) return storageDevice.DisplayName;
        if (!string.IsNullOrWhiteSpace(storageDevice.ProductName)) return storageDevice.ProductName;

        return "Storage Device";
    }

    private static byte GetNvmeTemperatureSensorAttributeIdentifier(int thermalSensorIndex) => (byte)(NvmeTemperatureSensorAttributeBaseIdentifier + thermalSensorIndex);

    private static double ConvertBytesToGigabytes(ulong bytes) => bytes / BytesPerGigabyte;

    private static float ConvertKelvinToCelsius(ulong kelvin) => kelvin > 0 ? (float)kelvin - 273.15f : 0;

    /// <summary>
    /// Helper to calculate the disk performance with base timestamps
    /// https://docs.microsoft.com/en-us/windows/win32/cimwin32prov/win32-perfrawdata
    /// </summary>
    private class PerformanceValue
    {
        public double Result { get; private set; }

        private long Time { get; set; }

        private long Value { get; set; }

        public void Update(long value, long valueBase)
        {
            long differenceValue = value - Value;
            long differenceTime = valueBase - Time;

            Value = value;
            Time = valueBase;
            Result = 100.0 / differenceTime * differenceValue;

            // sometimes it is possible that difference value > difference time base
            // limit result to 100%, this is because timing issues during read from pcie controller an latency between IO operation
            if (Result > 100) Result = 100;
        }
    }
}

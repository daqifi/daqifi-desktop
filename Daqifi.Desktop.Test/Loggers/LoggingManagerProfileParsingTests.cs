using System.Xml.Linq;
using Daqifi.Desktop.Logger;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Covers <see cref="LoggingManager.ParseProfiles"/>, the parse half of
/// <see cref="LoggingManager.LoadProfilesFromXml"/>. The profile-settings XML is user-writable on
/// disk, so these fix the contract for malformed entries: one bad entry must not abort the load, and
/// an entry the app could never save or unsubscribe again must not be materialized at all.
/// </summary>
[TestClass]
public class LoggingManagerProfileParsingTests
{
    private const string PROFILE_ID_A = "11111111-1111-1111-1111-111111111111";
    private const string PROFILE_ID_B = "22222222-2222-2222-2222-222222222222";

    [TestMethod]
    public void ParseProfiles_ReadsProfileDevicesAndActiveChannels()
    {
        var doc = XDocument.Parse($"""
            <Profiles>
              <Profile>
                <Name>Bench</Name>
                <ProfileID>{PROFILE_ID_A}</ProfileID>
                <CreatedOn>2026-01-02T03:04:05</CreatedOn>
                <Devices>
                  <Device>
                    <DeviceName>Nq3</DeviceName>
                    <DevicePartNumber>Nq3-1</DevicePartNumber>
                    <MACAddress>AA:BB:CC:DD:EE:FF</MACAddress>
                    <DeviceSerialNo>SN-1</DeviceSerialNo>
                    <SamplingFrequency>100</SamplingFrequency>
                    <Channels>
                      <Channel>
                        <Name>AI0</Name>
                        <Type>Analog</Type>
                        <IsActive>true</IsActive>
                      </Channel>
                    </Channels>
                  </Device>
                </Devices>
              </Profile>
            </Profiles>
            """);

        var profiles = LoggingManager.ParseProfiles(doc);

        Assert.AreEqual(1, profiles.Count);
        var profile = profiles[0];
        Assert.AreEqual("Bench", profile.Name);
        Assert.AreEqual(Guid.Parse(PROFILE_ID_A), profile.ProfileId);
        Assert.AreEqual(new DateTime(2026, 1, 2, 3, 4, 5), profile.CreatedOn);

        Assert.AreEqual(1, profile.Devices.Count);
        var device = profile.Devices[0];
        Assert.AreEqual("Nq3", device.DeviceName);
        Assert.AreEqual("Nq3-1", device.DevicePartName);
        Assert.AreEqual("AA:BB:CC:DD:EE:FF", device.MacAddress);
        Assert.AreEqual("SN-1", device.DeviceSerialNo);
        Assert.AreEqual(100, device.SamplingFrequency);

        Assert.AreEqual(1, device.Channels.Count);
        var channel = device.Channels[0];
        Assert.AreEqual("AI0", channel.Name);
        Assert.AreEqual("Analog", channel.Type);
        Assert.IsTrue(channel.IsChannelActive);
        // The channel carries its device's serial so a legend entry can be attributed without
        // walking back up to the device.
        Assert.AreEqual("SN-1", channel.SerialNo);
    }

    [TestMethod]
    public void ParseProfiles_DefaultsOptionalElements_WhenMissing()
    {
        // Only <ProfileID> is required; the writer omits <Channels> for a device with no active
        // channels, and a hand-edited file can be missing anything else.
        var doc = XDocument.Parse($"""
            <Profiles>
              <Profile>
                <ProfileID>{PROFILE_ID_A}</ProfileID>
                <Devices>
                  <Device>
                    <DeviceName>Nq3</DeviceName>
                  </Device>
                </Devices>
              </Profile>
            </Profiles>
            """);

        var profiles = LoggingManager.ParseProfiles(doc);

        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual(string.Empty, profiles[0].Name);
        Assert.AreEqual(DateTime.MinValue, profiles[0].CreatedOn);

        var device = profiles[0].Devices[0];
        Assert.AreEqual(string.Empty, device.DevicePartName);
        Assert.AreEqual(string.Empty, device.MacAddress);
        Assert.AreEqual(string.Empty, device.DeviceSerialNo);
        Assert.AreEqual(0, device.SamplingFrequency);
        Assert.AreEqual(0, device.Channels.Count);
    }

    [TestMethod]
    public void ParseProfiles_ReturnsEmpty_WhenDocumentHasNoProfiles()
    {
        var profiles = LoggingManager.ParseProfiles(XDocument.Parse("<Profiles />"));

        Assert.AreEqual(0, profiles.Count);
    }

    [TestMethod]
    public void ParseProfiles_SkipsProfile_WhenProfileIdElementIsMissing()
    {
        // A profile with no <ProfileID> cannot be matched back to its XML node by
        // UpdateProfileInXml or the AddAndRemoveProfileXml remove path, so loading it would put an
        // entry in the UI that silently fails every save and unsubscribe against it.
        var doc = XDocument.Parse("""
            <Profiles>
              <Profile>
                <Name>No identity</Name>
              </Profile>
            </Profiles>
            """);

        var profiles = LoggingManager.ParseProfiles(doc);

        Assert.AreEqual(0, profiles.Count);
    }

    [TestMethod]
    public void ParseProfiles_SkipsProfile_WhenProfileIdIsNotAGuid()
    {
        var doc = XDocument.Parse("""
            <Profiles>
              <Profile>
                <Name>Corrupt identity</Name>
                <ProfileID>not-a-guid</ProfileID>
              </Profile>
            </Profiles>
            """);

        var profiles = LoggingManager.ParseProfiles(doc);

        Assert.AreEqual(0, profiles.Count);
    }

    [TestMethod]
    public void ParseProfiles_KeepsProfile_WhenProfileIdIsExplicitlyAllZeroes()
    {
        // An all-zero ID the file spells out is still matchable by the XML operations, so it is a
        // valid identity — only a missing or unparsable one is not.
        var doc = XDocument.Parse($"""
            <Profiles>
              <Profile>
                <Name>Zeroed</Name>
                <ProfileID>{Guid.Empty}</ProfileID>
              </Profile>
            </Profiles>
            """);

        var profiles = LoggingManager.ParseProfiles(doc);

        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual(Guid.Empty, profiles[0].ProfileId);
    }

    [TestMethod]
    public void ParseProfiles_SkipsOnlyTheMalformedEntry_AndKeepsTheRest()
    {
        // The regression this parse path exists for: one malformed <Profile> used to throw out of
        // the XElement cast operators and abort the entire profile load.
        var doc = XDocument.Parse($"""
            <Profiles>
              <Profile>
                <Name>First</Name>
                <ProfileID>{PROFILE_ID_A}</ProfileID>
              </Profile>
              <Profile>
                <Name>Malformed</Name>
              </Profile>
              <Profile>
                <Name>Last</Name>
                <ProfileID>{PROFILE_ID_B}</ProfileID>
              </Profile>
            </Profiles>
            """);

        var profiles = LoggingManager.ParseProfiles(doc);

        Assert.AreEqual(2, profiles.Count);
        Assert.AreEqual("First", profiles[0].Name);
        Assert.AreEqual(Guid.Parse(PROFILE_ID_A), profiles[0].ProfileId);
        Assert.AreEqual("Last", profiles[1].Name);
        Assert.AreEqual(Guid.Parse(PROFILE_ID_B), profiles[1].ProfileId);
    }

    [TestMethod]
    public void ParseProfiles_DoesNotProduceCollidingIds_ForMultipleMalformedEntries()
    {
        // Defaulting a missing ID to Guid.Empty also gave every malformed entry the SAME identity,
        // so a lookup by ID could not tell them apart even in memory.
        var doc = XDocument.Parse("""
            <Profiles>
              <Profile><Name>Malformed A</Name></Profile>
              <Profile><Name>Malformed B</Name></Profile>
            </Profiles>
            """);

        var profiles = LoggingManager.ParseProfiles(doc);

        Assert.AreEqual(0, profiles.Count);
        Assert.AreEqual(profiles.Count, profiles.Select(p => p.ProfileId).Distinct().Count());
    }
}

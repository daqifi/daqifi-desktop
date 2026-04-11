using Daqifi.Desktop.Logger;

namespace Daqifi.Desktop.Test.Loggers;

[TestClass]
public class LoggingSessionTests
{
    [TestMethod]
    public void Name_Setter_ShouldRaisePropertyChanged()
    {
        var session = new LoggingSession { ID = 7, Name = "Session_7" };
        var propertyChangedFired = false;

        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoggingSession.Name))
            {
                propertyChangedFired = true;
            }
        };

        session.Name = "Renamed Session";

        Assert.IsTrue(propertyChangedFired);
        Assert.AreEqual("Renamed Session", session.Name);
    }

    [TestMethod]
    public void Name_Getter_ShouldFallbackToSessionId_WhenBlank()
    {
        var session = new LoggingSession { ID = 7, Name = string.Empty };

        Assert.AreEqual("Session 7", session.Name);
    }
}

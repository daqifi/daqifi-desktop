using System.Globalization;
using System.Windows.Data;
using Daqifi.Desktop.Helpers;

namespace Daqifi.Desktop.Test.Converters
{
    [TestClass]
    public class EnumDescriptionConverterTests
    {
        private IValueConverter _converter;

        // Test enum with Description attributes
        private enum TestEnum
        {
            [System.ComponentModel.Description("First Option Description")]
            FirstOption,
            
            [System.ComponentModel.Description("Second Option Description")]
            SecondOption,
            
            // No description attribute
            ThirdOption,
            
            [System.ComponentModel.Description("")]
            EmptyDescription
        }

        // Test enum without any Description attributes
        private enum SimpleEnum
        {
            Value1,
            Value2,
            Value3
        }

        [TestInitialize]
        public void Setup()
        {
            _converter = new EnumDescriptionConverter();
        }

        [TestMethod]
        public void Convert_NullValue_ReturnsEmptyString()
        {
            // Arrange
            object value = null;

            // Act
            var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void Convert_NonEnumValue_ReturnsToString()
        {
            // Arrange
            var testValues = new object[]
            {
                "Regular String",
                123,
                45.67,
                true,
                new DateTime(2024, 1, 1)
            };

            foreach (var value in testValues)
            {
                // Act
                var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

                // Assert
                Assert.AreEqual(value.ToString(), result, $"Failed for value: {value}");
            }
        }

        [TestMethod]
        public void Convert_EnumWithDescription_ReturnsDescription()
        {
            // Arrange
            var enumValue = TestEnum.FirstOption;

            // Act
            var result = _converter.Convert(enumValue, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual("First Option Description", result);
        }

        [TestMethod]
        public void Convert_EnumWithoutDescription_ReturnsEnumName()
        {
            // Arrange
            var enumValue = TestEnum.ThirdOption;

            // Act
            var result = _converter.Convert(enumValue, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual("ThirdOption", result);
        }

        [TestMethod]
        public void Convert_EnumWithEmptyDescription_ReturnsEmptyString()
        {
            // Arrange
            var enumValue = TestEnum.EmptyDescription;

            // Act
            var result = _converter.Convert(enumValue, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void Convert_SimpleEnumWithoutAnyDescriptions_ReturnsEnumName()
        {
            // Arrange
            var testValues = new[] { SimpleEnum.Value1, SimpleEnum.Value2, SimpleEnum.Value3 };

            foreach (var enumValue in testValues)
            {
                // Act
                var result = _converter.Convert(enumValue, typeof(string), null, CultureInfo.InvariantCulture);

                // Assert
                Assert.AreEqual(enumValue.ToString(), result, $"Failed for enum value: {enumValue}");
            }
        }

        [TestMethod]
        public void Convert_DifferentCultures_ReturnsConsistentResult()
        {
            // Arrange
            var enumValue = TestEnum.SecondOption;
            var cultures = new[]
            {
                CultureInfo.InvariantCulture,
                new CultureInfo("en-US"),
                new CultureInfo("fr-FR"),
                new CultureInfo("de-DE")
            };

            foreach (var culture in cultures)
            {
                // Act
                var result = _converter.Convert(enumValue, typeof(string), null, culture);

                // Assert
                Assert.AreEqual("Second Option Description", result, $"Failed for culture: {culture.Name}");
            }
        }

        [TestMethod]
        public void ConvertBack_Always_ReturnsEmptyString()
        {
            // Arrange
            var testValues = new object[]
            {
                "Some Description",
                null,
                123,
                TestEnum.FirstOption
            };

            foreach (var value in testValues)
            {
                // Act
                var result = _converter.ConvertBack(value, typeof(Enum), null, CultureInfo.InvariantCulture);

                // Assert
                Assert.AreEqual(string.Empty, result, $"Failed for value: {value}");
            }
        }

        [TestMethod]
        public void Convert_BoxedEnum_ReturnsDescription()
        {
            // Arrange
            object boxedEnum = TestEnum.FirstOption;

            // Act
            var result = _converter.Convert(boxedEnum, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual("First Option Description", result);
        }

        [TestMethod]
        public void Convert_NullableEnumWithValue_ReturnsDescription()
        {
            // Arrange
            TestEnum? nullableEnum = TestEnum.SecondOption;

            // Act
            var result = _converter.Convert(nullableEnum, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual("Second Option Description", result);
        }

        [TestMethod]
        public void Convert_NullableEnumWithNull_ReturnsEmptyString()
        {
            // Arrange
            TestEnum? nullableEnum = null;

            // Act
            var result = _converter.Convert(nullableEnum, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }
    }
}
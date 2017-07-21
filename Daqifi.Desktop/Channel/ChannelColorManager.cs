using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Daqifi.Desktop.Channel
{
    public class ChannelColorManager
    {
        #region Private Data
        //TODO this should be plunty of colors for now but at some point need a way to make it expandable
        private readonly List<Brush> _brushes = new List<Brush>() {
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#d24d57"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f22613"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#d91e18"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#96281b"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#ef4836"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#d64541"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#c0392b"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#cf000f"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#e74c3c"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#db0a5b"),
                          
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f64747"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f1a9a0"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#d2527f"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#e08283"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f62459"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#e26a6a"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#dcc6e0"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#663399"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#674172"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#aea8d3"),

                          (SolidColorBrush)new BrushConverter().ConvertFrom("#913d88"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#9a12b3"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#bf55ec"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#be90d4"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#8e44ad"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#9b59b6"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#446cb3"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#e4f1fe"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#4183d7"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#59abe3"),
                          
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#81cfe0"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#52b3d9"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#c5eff7"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#22a7f0"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#3498db"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#2c3e50"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#19b5fe"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#336e7b"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#22313f"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#6bb9f0"),
                          
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#1e8bc3"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#3a539b"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#34495e"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#67809f"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#2574a9"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#1f3a93"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#89c4f4"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#4b77be"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#5c97bf"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#4ecdc4"),
                          
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#a2ded0"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#87d37c"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#90c695"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#26a65b"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#03c9a9"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#68c3a3"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#65c6bb"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#1bbc9b"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#1ba39c"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#66cc99"),
                          
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#36d7b7"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#c8f7c5"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#86e2d5"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#2ecc71"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#16a085"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#3fc380"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#019875"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#03a678"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#4daf7c"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#2abb9b"),
                          
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#00b16a"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#1e824c"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#049372"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#26c281"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#fde3a7"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f89406"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#eb9532"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#e87e04"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f4b350"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f2784b"),
                          
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#eb974e"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f5ab35"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#d35400"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f39c12"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f9690e"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f9bf3b"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f27935"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#e67e22"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#ececec"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#6c7a89"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#d2d7d3"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#eeeeee"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#bdc3c7"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#ecf0f1"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#95a5a6"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#dadfe1"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#abb7b7"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f2f1ff"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#abb7b7"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#f2f1ef"),
                          (SolidColorBrush)new BrushConverter().ConvertFrom("#bfbfbf")};

        private readonly List<Brush> _registeredBrushes;
        private readonly Random _randomGenerator;
        #endregion

        #region Properties
        public List<Brush> Brushes
        {
            get { return _brushes; }
        }
        #endregion

        #region Singleton Constructor / Initalization
        private static readonly ChannelColorManager _instance = new ChannelColorManager();

        private ChannelColorManager()
        {
            foreach (var brush in Brushes) brush.Freeze();
            _registeredBrushes = new List<Brush>();
            _randomGenerator = new Random();
        }

        public static ChannelColorManager Instance
        {
            get { return _instance; }
        }
        #endregion

        public Brush NewColor()
        {
            //TODO for now this just returns a color potentially might maintain a dictionary of what is being assigned each color
            int randomIndex = _randomGenerator.Next(0, _brushes.Count);
            var newColor = _brushes.ElementAt(randomIndex);
            _brushes.RemoveAt(randomIndex);
            _registeredBrushes.Add(newColor);
            return newColor;
        }
    }
}

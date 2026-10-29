using System.Globalization;
using System.Windows.Data;

namespace Perialls;

// Diz se dois valores são iguais (ex.: o número de um botão e o valor atual do aparelho).
// Compara como texto, para 4 (int) e 4 (byte) contarem como iguais.
public sealed class SameValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] != null && Equals(values[0]?.ToString(), values[1]?.ToString());

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

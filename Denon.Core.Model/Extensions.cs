using System;
using System.Collections.Generic;
using System.Linq;

namespace Denon.Core.Extensions
{
    public static class ControlExtensions
    {

        public static string GetResponseItemValue(this List<Denon.Core.Model.Control.ResponseItem> responseItems, string name)
        {
            string returnValue = default;

            if (responseItems.Exists(i => i.Name.ToLower() == name))
            {
                returnValue = responseItems.Where(i => i.Name.ToLower() == name).FirstOrDefault().Value;
            }

            return returnValue;
        }


        public static T GetEnumFromString<T>(this string value)
        {
            T returnValue;

            try
            {
                if (value is null)
                {
                    returnValue = (T)Enum.GetValues(typeof(T)).GetValue(0);
                }
                else
                {
                    returnValue = (T)Enum.Parse(typeof(T), value, true);
                }
            }
            catch
            {
                returnValue = (T)Enum.GetValues(typeof(T)).GetValue(0);
            }

            return returnValue;
        }

    }

}

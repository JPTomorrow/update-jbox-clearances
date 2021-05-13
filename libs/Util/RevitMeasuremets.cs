using Autodesk.Revit.DB;
using JPMorrow.Revit.Documents;
using JPMorrow.Revit.Text;

namespace JPMorrow.Revit.Measurements {
    public static class RMeasure {
#if REVIT2017 || REVIT2018 || REVIT2019 || REVIT2020 // DisplayUnitType Depreciated
        public static double LengthDbl(Document doc, string cvt_str) {
            bool s = UnitFormatUtils.TryParse(info.DOC.GetUnits(), UnitType.UT_Length, cvt_str, out double val);
            return s ? val : -1;
        }

        public static double AngleDbl(Document doc, string angle_str) {
            bool s = UnitFormatUtils.TryParse(doc.GetUnits(), UnitType.UT_Angle, angle_str, out double val);
            return s ? val : -1;
        }

        public static string LengthFromDbl(Document doc, double dbl) {
            return UnitFormatUtils.Format(doc.GetUnits(), UnitType.UT_Length, dbl, true, false, CustomFormatValue.FeetAndInches);
        }

        public static string AngleFromDouble(Document doc, double dbl) {
            return UnitFormatUtils.Format(doc.GetUnits(), UnitType.UT_Angle, dbl, true, false, CustomFormatValue.Angle);
        }    
#else // ForgeTypeId updated
        public static double LengthDbl(Document doc, string cvt_str) {
            bool s = UnitFormatUtils.TryParse(doc.GetUnits(), UnitTypeId.FeetFractionalInches, cvt_str, out double val);
            return s ? val : -1;
        }
        
        public static double AngleDbl(Document doc, string angle_str) {
            bool s = UnitFormatUtils.TryParse(doc.GetUnits(), UnitTypeId.Degrees, angle_str, out double val);
            return s ? val : -1;
        }

        public static string LengthFromDbl(Document doc, double dbl) {
            return UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, dbl, false, CustomFormatValue.FeetAndInches);
        }

        public static string AngleFromDouble(Document doc, double dbl) {
            return UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Angle, dbl, false, CustomFormatValue.Angle);
        }
#endif
    }
}

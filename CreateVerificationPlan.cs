////////////////////////////////////////////////////////////////////////////////
// CreateVerificationPlan.cs
//
//  A ESAPI v13.6+ script that demonstrates creation of verification plans 
//  from a clinical plan.
//
// Kata Intermediate.5)    
//  Program an ESAPI automation script that creates a new QA course, a new set 
//  of verification plans for the selected clinical plan 
//  (1 composite and 1 verification plan per beam), and calculates dose for all 
//  of the new verification plans.
//
// Applies to:
//      Eclipse Scripting API for Research Users
//          13.6, 13.7, 15.0,15.1
//      Eclipse Scripting API
//          15.1
//
// Copyright (c) 2017 Varian Medical Systems, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
//
//  The above copyright notice and this permission notice shall be included in 
//  all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
////////////////////////////////////////////////////////////////////////////////
// #define v136 // uncomment this for v13.6 or v13.7
using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

#if !v136
// for 15.1 script approval so approval wizard knows this is a writeable script.
[assembly: ESAPIScript(IsWriteable = true)]
#endif

namespace VMS.TPS
{
    public class Script
    {
        // these three strings define the patient/study/image id for the image phantom that will be copied into the active patient.
        public static string QAPatientID_Trilogy = "ArcCheck";
        public static string QAStudyID_Trilogy = "CT1";
        public static string QAImageID_Trilogy = "ArcCheck";

        public static string QAPatientID_iX = "iX5925";
        public static string QAStudyID_iX = "1";
        public static string QAImageID_iX = "MapCheck2";

        public Script()
        {
        }

        public void Execute(ScriptContext context /*, System.Windows.Window window, ScriptEnvironment environment*/)
        {
            Patient p = context.Patient;
            if (p == null)
                throw new ApplicationException("Please load a patient");

            ExternalPlanSetup plan = context.ExternalPlanSetup;
            if (plan == null)
                throw new ApplicationException("Please load an external beam plan that will be verified.");

            p.BeginModifications();
            // TODO: look whether the phantom scan exists in this patient before copying it
            StructureSet ssQA;
            if (plan.Beams.FirstOrDefault().TreatmentUnit.Name == "Trilogy")
                ssQA = p.CopyImageFromOtherPatient(QAPatientID_Trilogy, QAStudyID_Trilogy, QAImageID_Trilogy);
            else
            {
                if (plan.Beams.FirstOrDefault().TreatmentUnit.Id == "iX5925")
                    ssQA = p.CopyImageFromOtherPatient(QAPatientID_iX, QAStudyID_iX, QAImageID_iX);
                else
                {
                    MessageBox.Show(string.Format("Treatment machine {0} in plan not recognized.", plan.Beams.FirstOrDefault().TreatmentUnit.Id));
                    ssQA = null;
                }
            }

            // Get or create course with Id 'IMRTQA'
            const string courseId = "QA";
            Course course = p.Courses.Where(o => o.Id == courseId).SingleOrDefault();
            if (course == null)
            {
                course = p.AddCourse();
                course.Id = courseId;
            }
#if false
        // Create an individual verification plan for each field.
        foreach (var beam in plan.Beams)
        {
            CreateVerificationPlan(course, new List<Beam> { beam }, plan, ssQA, beam.Id, calculateDose: false);
        }
#endif

            string verificationId = "";
            if (plan.Id.Length >= 13)  //if over 13 chars in Id
                verificationId = plan.Id.Substring(0, (plan.Id.Length - 1)) + "A";
            else
                verificationId = plan.Id + "A";

            // Create a verification plan that contains all fields (Composite).
            ExternalPlanSetup verificationPlan = CreateVerificationPlan(course, plan.Beams, plan, ssQA, verificationId, calculateDose: true);

            // nagivate back from verificationPlan to verified plan
            PlanSetup verifiedPlan = verificationPlan.VerifiedPlan;
            if (plan != verifiedPlan)
            {
                MessageBox.Show(string.Format("ERROR! verified plan {0} != loaded plan {1}", verifiedPlan.Id
                    , plan.Id));
            }
            MessageBox.Show(string.Format("Success - verification plan {0} created in course {1}.", verificationPlan.Id, course.Id));

        }
        /// <summary>
        /// Create verifications plans for a given treatment plan.
        /// </summary>
        public static ExternalPlanSetup CreateVerificationPlan(Course course, IEnumerable<Beam> beams, ExternalPlanSetup verifiedPlan, StructureSet verificationStructures,
                                                   string planId, bool calculateDose)
        {
            var verificationPlan = course.AddExternalPlanSetupAsVerificationPlan(verificationStructures, verifiedPlan);
            
            verificationPlan.Id = planId;

            // Put isocenter to the center of the QAdevice
            VVector isocenter = verificationStructures.Image.UserOrigin;
            foreach (Beam beam in beams)
            {
                ExternalBeamMachineParameters MachineParameters =
                    new ExternalBeamMachineParameters(beam.TreatmentUnit.Id, beam.EnergyModeDisplayName, beam.DoseRate, beam.Technique.Id, string.Empty);
                
                if (beam.MLCPlanType.ToString() == "VMAT")
                {
                    // Create a new VMAT beam.
                    var collimatorAngle = beam.ControlPoints.First().CollimatorAngle;
                    var gantryAngleStart = beam.ControlPoints.First().GantryAngle;
                    var gantryAngleEnd = beam.ControlPoints.Last().GantryAngle;
                    var gantryDirection = beam.GantryDirection;
                    var couchAngle = 0.0;
                    var metersetWeights = beam.ControlPoints.Select(cp => cp.MetersetWeight);
                    var beamWeight = beam.WeightFactor;
                    verificationPlan.AddVMATBeam(MachineParameters, metersetWeights, collimatorAngle, gantryAngleStart,
                        gantryAngleEnd, gantryDirection, couchAngle, isocenter);
                    beam.Id = "G" + gantryAngleStart + " " + "T" + couchAngle;  //these need to match for verification and treat plan
                    continue;
                }

                if (beam.MLCPlanType.ToString() == "DoseDynamic")
                {
                    // Create a new IMRT beam.
                    double gantryAngle;
                    double collimatorAngle;
                    if (beam.TreatmentUnit.Name == "Trilogy")
                    {
                        gantryAngle = beam.ControlPoints.First().GantryAngle;
                        collimatorAngle = beam.ControlPoints.First().CollimatorAngle;
                    }

                    else //ix with only mapcheck
                    {
                        gantryAngle = 0.0;
                        collimatorAngle = 0.0;
                    }

                    var couchAngle = 0.0;
                    var metersetWeights = beam.ControlPoints.Select(cp => cp.MetersetWeight);
                    verificationPlan.AddSlidingWindowBeam(MachineParameters, metersetWeights, collimatorAngle, gantryAngle,
                        couchAngle, isocenter);
                    beam.Id = "G" + gantryAngle + " " + "T" + couchAngle;  //these need to match for verification and treat plan
                    continue;
                }
                else
                {
                    var message = string.Format("Treatment field {0} is not VMAT or IMRT.", beam);
                    throw new Exception(message);
                }
            }

            foreach (Beam verificationBeam in verificationPlan.Beams)
            {
                var gantryAngle = verificationBeam.ControlPoints.First().GantryAngle;
                var couchAngle = verificationBeam.ControlPoints.First().PatientSupportAngle;
                verificationBeam.Id = "G" + gantryAngle + " " + "T" + couchAngle;  //these need to match for verification and treat plan
            }
            
            foreach (Beam verificationBeam in verificationPlan.Beams)
            {
                foreach(Beam beam in verifiedPlan.Beams)
                {
                    if (verificationBeam.Id == beam.Id)
                    {
                        if (verificationBeam.MLCPlanType.ToString() == "VMAT")
                        {
                            // Copy control points from the original beam.
                            var editableParams = beam.GetEditableParameters();
                            for (var n = 0; n < editableParams.ControlPoints.Count(); n++)
                            {
                                editableParams.ControlPoints.ElementAt(n).LeafPositions = beam.ControlPoints.ElementAt(n).LeafPositions;
                                editableParams.ControlPoints.ElementAt(n).JawPositions = beam.ControlPoints.ElementAt(n).JawPositions;
                                editableParams.WeightFactor = beam.WeightFactor;
                            }
                            verificationBeam.ApplyParameters(editableParams);
                            continue;
                        }
                        if (verificationBeam.MLCPlanType.ToString() == "DoseDynamic")
                        {
                            var editableParams = beam.GetEditableParameters();
                            for (var n = 0; n < editableParams.ControlPoints.Count(); n++)
                            {
                                editableParams.ControlPoints.ElementAt(n).LeafPositions = beam.ControlPoints.ElementAt(n).LeafPositions;
                                editableParams.ControlPoints.ElementAt(n).JawPositions = beam.ControlPoints.ElementAt(n).JawPositions;
                            }
                            verificationBeam.ApplyParameters(editableParams);
                            continue;
                        }
                    }
                }
            }
            // Set presciption
            const int numberOfFractions = 1;
            verificationPlan.SetPrescription(numberOfFractions, verifiedPlan.DosePerFraction, treatmentPercentage: 1.0);

            verificationPlan.SetCalculationModel(CalculationType.PhotonVolumeDose, verifiedPlan.GetCalculationModel(CalculationType.PhotonVolumeDose));

            var res = verificationPlan.CalculateDose();
            if (!res.Success)
            {
                var message = string.Format("Dose calculation failed for verification plan. Output:\n{0}", res);
                throw new Exception(message);
            }
            return verificationPlan;
        }

        /// <summary>
        /// Create a copy of an existing beam (beams are unique to plans).
        /// </summary>
        private void CopyBeams(IEnumerable<Beam> originalBeams, ExternalPlanSetup verificationPlan, VVector isocenter, bool getCollimatorAndGantryFromBeam)
        {
            foreach (Beam originalBeam in originalBeams)
            {
                ExternalBeamMachineParameters MachineParameters =
                    new ExternalBeamMachineParameters(originalBeam.TreatmentUnit.Id, originalBeam.EnergyModeDisplayName, originalBeam.DoseRate, originalBeam.Technique.Id, string.Empty);

                if (originalBeam.MLCPlanType.ToString() == "VMAT")
                {
                    //plan.Course.cop
                    // Create a new VMAT beam.
                    var collimatorAngle = getCollimatorAndGantryFromBeam ? originalBeam.ControlPoints.First().CollimatorAngle : 0.0;
                    var gantryAngleStart = getCollimatorAndGantryFromBeam ? originalBeam.ControlPoints.First().GantryAngle : 0.0;
                    var gantryAngleEnd = getCollimatorAndGantryFromBeam ? originalBeam.ControlPoints.Last().GantryAngle : 0.0;
                    var gantryDirection = originalBeam.GantryDirection;
                    //var couchAngle = getCollimatorAndGantryFromBeam ? originalBeam.ControlPoints.First().PatientSupportAngle : 0.0;
                    var couchAngle = 0.0;  //mapcheck and arccheck uses no couch kick
                    var metersetWeights = originalBeam.ControlPoints.Select(cp => cp.MetersetWeight);
                    var beamWeight = originalBeam.WeightFactor;
                    var beam = verificationPlan.AddVMATBeam(MachineParameters, metersetWeights, collimatorAngle, gantryAngleStart,
                        gantryAngleEnd, gantryDirection, couchAngle, isocenter);
                    // Copy control points from the original beam.
                    var editableParams = beam.GetEditableParameters();
                    MessageBox.Show(beam.ControlPoints.Count().ToString());
                    for (var i = 0; i < editableParams.ControlPoints.Count(); i++)
                    {
                        editableParams.ControlPoints.ElementAt(i).LeafPositions = originalBeam.ControlPoints.ElementAt(i).LeafPositions;
                        editableParams.ControlPoints.ElementAt(i).JawPositions = originalBeam.ControlPoints.ElementAt(i).JawPositions;
                        editableParams.WeightFactor = originalBeam.WeightFactor;
                    }
                    beam.ApplyParameters(editableParams);
                    beam.Id = "G" + gantryAngleStart + " " + "T" + couchAngle;
                }
                if (originalBeam.MLCPlanType.ToString() == "DoseDynamic")
                {
                    // Create a new IMRT beam.
                    //var gantryAngle = getCollimatorAndGantryFromBeam ? originalBeam.ControlPoints.First().GantryAngle : 0.0;
                    double gantryAngle;
                    double collimatorAngle;
                    if (originalBeam.TreatmentUnit.Name == "Trilogy")
                    {
                        gantryAngle = getCollimatorAndGantryFromBeam ? originalBeam.ControlPoints.First().GantryAngle : 0.0;
                        collimatorAngle = getCollimatorAndGantryFromBeam ? originalBeam.ControlPoints.First().CollimatorAngle : 0.0;
                    }

                    else //ix and only mapcheck
                    {
                        gantryAngle = 0.0;
                        collimatorAngle = 0.0;
                    }

                    //var couchAngle = getCollimatorAndGantryFromBeam ? originalBeam.ControlPoints.First().PatientSupportAngle : 0.0;
                    var couchAngle = 0.0;
                    var metersetWeights = originalBeam.ControlPoints.Select(cp => cp.MetersetWeight);
                    var beam = verificationPlan.AddSlidingWindowBeam(MachineParameters, metersetWeights, collimatorAngle, gantryAngle,
                        couchAngle, isocenter);
                    // Copy control points from the original beam.
                    var editableParams = beam.GetEditableParameters();
                    for (var i = 0; i < editableParams.ControlPoints.Count(); i++)
                    {
                        editableParams.ControlPoints.ElementAt(i).LeafPositions = originalBeam.ControlPoints.ElementAt(i).LeafPositions;
                        editableParams.ControlPoints.ElementAt(i).JawPositions = originalBeam.ControlPoints.ElementAt(i).JawPositions;
                    }
                    beam.ApplyParameters(editableParams);
                    beam.Id = "G" + gantryAngle + " " + "T" + couchAngle;
                }
                else
                {
                    var message = string.Format("Treatment field {0} is not VMAT or IMRT.", originalBeam);
                    throw new Exception(message);
                }
            }
        }
    }
}
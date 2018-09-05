using System;
using RLBotDotNet;
using rlbot.flat;
using System.Diagnostics;

namespace RLBotCSharpExample
{
    // We want to our bot to derive from Bot, and then implement its abstract methods
    class ExampleBot : Bot
    {
        // We want the constructor for ExampleBot to extend from Bot
        public ExampleBot(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex)
        {
            kickoffWatch.Reset();
            kickoffWatch.Start();

            dodgeWatch.Reset();
            dodgeWatch.Start();
        }

        private static Stopwatch kickoffWatch = new Stopwatch();
        private static Stopwatch dodgeWatch = new Stopwatch();
        private static Boolean kickoff = false;

        public override Controller GetOutput(GameTickPacket gameTickPacket)
        {
            // This controller object will be returned at the end of the method
            // This controller will contain all the inputs that we want the bot to perform
            Controller controller = new Controller();

            // Wrap gameTickPacket retrieving in a try-catch so that the bot doesn't crash whenever a value isn't present
            // A value may not be present if it was not sent
            // These are nullables so trying to get them when they're null will cause errors, therefore we wrap in try-catch
            try
            {
                // Store the required data from the "gameTickPacket"
                Vector3 ballLocation = gameTickPacket.Ball.Value.Physics.Value.Location.Value;
                Vector3 carLocation = gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value;
                Rotator carRotation = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value;

                // Calculate the distance from the car to the ball
                var distanceToBall = Get2DDistance(carLocation.X, ballLocation.X, carLocation.Y, carLocation.Y);

                // Calculate to get the angle from the front of the bot's car to the ball
                double botToTargetAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);
                double botFrontToTargetAngle = botToTargetAngle - carRotation.Yaw;
                
                // Decide which way to steer in order to get to the ball
                float steer = (float)(botFrontToTargetAngle / Math.PI) * 2.5F;
                controller.Steer = steer;
                Console.Write(steer);

                // Change the throttle to so the bot can move
                controller.Throttle = (3F - Math.Abs(steer));

                // Handle sliding
                controller.Handbrake = (Math.Abs(steer) > 2.75);

                // Handle boosting
                controller.Boost = (Math.Abs(steer) < 0.15F && carLocation.Z < 120);

                // Kickoff
                Boolean kickoff = ballLocation.X == 0 && ballLocation.Y == 0;
                if (kickoff)
                {
                    // Left Corner Spawn
                    if ((int)carLocation.X == 2043 && (int)carLocation.Y == -2555)
                    {
                        // DistanceToBall = 3271                         
                    }
                    // Right Corner Spawn 
                    else if ((int)carLocation.X == -2043 && (int)carLocation.Y == -2555)
                    {
                        // DistanceToBall = 3271
                    }
                    // Back Left Spawn
                    else if ((int)carLocation.X == 256 && (int)carLocation.Y == -3833)
                    {
                        // DistanceToBall = 3842                        
                    }
                    // Back Right Spawn
                    else if ((int)Math.Round(carLocation.X) == -256 && (int)Math.Round(carLocation.Y) == -3833)
                    {
                        // DistanceToBall = 3842                        
                    }
                    // Far Back Center Spawn
                    else if ((int)carLocation.X == 0 && (int)carLocation.Y == -4601)
                    {
                        // DistanceToBall = 4601
                    }
                    
                    if (!kickoff)
                    {
                        kickoffWatch.Reset();
                        kickoffWatch.Start();
                    }

                    // Pause on kickoff for the first second
                    if (kickoffWatch.ElapsedMilliseconds <= 5000)
                    {
                        controller.Boost = false;
                        controller.Throttle = 0;
                        Console.Write(", " + (kickoffWatch.ElapsedMilliseconds / 1000F) + "s kickoff");
                    }

                    kickoff = true;
                }
                else
                {
                    kickoff = false;
                    kickoffWatch.Stop();
                }

                // Handle dodging
                Console.WriteLine(", " + (dodgeWatch.ElapsedMilliseconds / 1000F) + "s dodge");
                if (!kickoff || gameTickPacket.Players(this.index).Value.Boost == 0)
                {
                    if (dodgeWatch.ElapsedMilliseconds <= 1000)
                    {
                        if (dodgeWatch.ElapsedMilliseconds <= 100)
                        {
                            controller.Jump = true;
                            controller.Pitch = -1;
                        }
                        else if (dodgeWatch.ElapsedMilliseconds >= 100 && dodgeWatch.ElapsedMilliseconds <= 150)
                        {
                            controller.Jump = false;
                            controller.Pitch = -1;
                        }
                        else if (dodgeWatch.ElapsedMilliseconds >= 150 && dodgeWatch.ElapsedMilliseconds <= 1000)
                        {
                            controller.Jump = true;
                            controller.Yaw = (float)Math.Sin(steer);
                            controller.Pitch = (float)-Math.Abs(Math.Cos(steer));
                        }
                    }
                    else if (Math.Abs(steer) < 0.2F && dodgeWatch.ElapsedMilliseconds >= 3000 && carLocation.Z < 120)
                    {
                        dodgeWatch.Restart();
                    }
                }

                // End the line printed this frame
                Console.WriteLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }

            return controller;
        }

        private static double CorrectAngle(double botFrontToTargetAngle)
        {
            // Correct the angle
            if(botFrontToTargetAngle < -Math.PI) botFrontToTargetAngle += 2 * Math.PI;
            if(botFrontToTargetAngle > Math.PI) botFrontToTargetAngle -= 2 * Math.PI;
            return botFrontToTargetAngle;
        }

        public double Get2DDistance(double x1, double x2, double y1, double y2)
        {
            return Math.Sqrt(Math.Pow((x2 - x1), 2) + Math.Pow((y2 - y1), 2));
        }

        public double magnitude(Vector3 vector)
        {
            return Math.Sqrt(Math.Pow((vector.X - vector.X), 2) + Math.Pow((vector.Y - vector.Y), 2));
        }

    }
}
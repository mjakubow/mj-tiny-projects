/*

FILE: VibratingStringFrame.java

DESCRIPTION: This file implements a Java frame-based simulation of a vibrating
  string (via real-time numerical solution of the wave equation).
  
  This implementation is intended as a self-contained sample of Java GUI and
  graphics programming.
  
NOTES:
  This code was originally written using a very early Java SDK (ca. 1996).  Since
  then, Java has undergone major transformations.  While the current source code
  has been updated from low-level AWT (applets and frames) to Swing (JFrame), some
  deprecated APIs may remain and will be removed in later versions.
  
  Compiling and running this program:
    - In the Eclipse IDE: Ctrl-F11 to build and run.
    - From the command line (assuming Java environment is set up):
      - Build: 'javac VibratingStringFrame.java'
      - Run: 'java VibratingStringFrame'
      
  Usage:
    - Start and stop string animation via the top buttons.
    - Adjust string shape by dragging with the mouse (whether or not animation is
      in progress).
    - For additional variation, change the damping and nonlinearity parameters via
      the top sliders.
      
  Background: See http://en.wikipedia.org/wiki/Wave_equation.
  
HISTORY:
  04/25/1996 - Started original AWT applet version.
  09/20/2014 - Started conversion to new Swing frame version.

CONTACT: mjakubow@outlook.com
  
*/

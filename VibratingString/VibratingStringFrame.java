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

import java.awt.*;
import java.awt.event.*;
import javax.swing.*;
import java.awt.geom.Line2D;


/// <summary>
/// This class implements the main window for graphical simulation of a vibrating
/// string.
/// </summary>
public class VibratingStringFrame extends JFrame implements Runnable
{
  //
  // Solution of wave equation with damping and the Fermi-Pasta-Ulam (FPU) cubic
  // nonlinearity
  //
	
  static final double PI = 3.141592653589793238462643383279502884197169399;
  
  // Number of discrete simulated points on the string.  Increase this value
  // for smoother string simulation, albeit at the cost of more processing.
  static final int GRID_LENGTH = 128;
  
  // Numerical solution grid
  double Y[][] = new double[GRID_LENGTH][3];
  int previousStep = 0, currentStep = 1, nextStep = 2;

  // Parameters for numerical solution.  These can be adjusted to experiment
  // with appearance of the simulation.
  double dtParameter = .1, dxParameter = .02,
    cParameter = .04, dampingParameter = 0.,
    fpuParameter = 0.;
  
  // Parameters for window display
  static int framePixelWidth = 1024, framePixelHeight = 768;  
  
  static double xDisplayScale = framePixelWidth/GRID_LENGTH,
    yDisplayScale = framePixelHeight/5.4,
    dampingScale = 100.,
    fpuScale = 100.;
  
  static int xDisplayOffset = (int)(0.5 + xDisplayScale/2),
    yDisplayOffset = framePixelHeight/2;  
  
  static final double MAX_DAMPING_VALUE = 1., MAX_FPU_VALUE = 1.;

  // User interface
  Thread solutionThread = null;
  
  int stringStatus;
  static final int STATUS_RUNNING = 0, STATUS_EDITING = 1;
  String statusText[] = new String[2];
   
  Scrollbar dampingSlider, fpuSlider;
  
  // Adjust this value to control simulation speed (in conjunction with the
  // grid length).
  int msRefreshDelay = 5;

  // Double buffering for smooth animation
  Dimension offDimension;
  Image offImage;
  Graphics2D offGraphics;

  
/// <summary>
/// Compute hyperbolic-secant function.
/// </summary>
protected double
sech(
  double x)
{
  return 2./(Math.exp(x) + Math.exp(-x));
}

/// <summary>
/// Initialize starting string shape.
/// </summary>
protected void
InitializeStringShape()
{
  for (int m = 0; m < GRID_LENGTH; m++)
    Y[m][previousStep] =
    Y[m][currentStep] =
      2.5*sech((double)(m - GRID_LENGTH/2)/5.)*Math.sin(m*10.*PI/(GRID_LENGTH - 1));
  
  Y[GRID_LENGTH - 1][previousStep] = 0.;
  Y[GRID_LENGTH - 1][currentStep] = 0.;
  Y[GRID_LENGTH - 1][nextStep] = 0.;
  Y[0][previousStep] = 0.;
  Y[0][currentStep] = 0.;
  Y[0][nextStep] = 0.;
}

/// <summary>
/// Prepare numerical solution grid for the next discrete time step.
/// <summary>
protected void
SwitchSolutionGrid()
{
  if (++previousStep > 2)
	previousStep = 0;
  
  if (++currentStep > 2)
	currentStep = 0;
  
  if (++nextStep > 2)
	nextStep = 0;  
}

/// <summary>
/// Calculate string shape at time t+dt from shape at time t
/// via a simple explicit finite-difference method.
/// </summary>
protected void
IterateSolutionGrid()
{
  double t1, t2;
  
  for (int m = 1; m < GRID_LENGTH - 1; m++)
  {
    t1 = Y[m+1][currentStep] - Y[m][currentStep];
    t2 = Y[m][currentStep] - Y[m-1][currentStep];
    
    Y[m][nextStep] =
      cParameter*cParameter*(dtParameter/dxParameter)*(dtParameter/dxParameter)*(Y[m-1][currentStep] -
      2*Y[m][currentStep] + Y[m+1][currentStep] + fpuParameter*(t1*t1 - t2*t2)) - Y[m][previousStep] +
      2*Y[m][currentStep] - dampingParameter*dtParameter*(Y[m][currentStep] - Y[m][previousStep]);
  }
  
  SwitchSolutionGrid();
}

/// <summary>
/// Set up user-interface components.
/// </summary>
public void
InitializeUserInterface()
{
  // In the applet context, 'this' is the main content pane.  In the frame context,
  // we need to obtain the content pane explicitly.
  Container pane = getContentPane();
	  
  setSize(framePixelWidth, framePixelHeight);
  
  // Set colors in the display frame.
  pane.setBackground(Color.black);
  pane.setForeground(Color.green);
  
  // Create the starting string shape.
  InitializeStringShape();
  
  statusText[STATUS_RUNNING] = "Running string simulation";
  statusText[STATUS_EDITING] = "Drag mouse to adjust string shape";
  stringStatus = STATUS_RUNNING;
  showStatus(statusText[stringStatus]);

  //
  // Create the user control interface.
  //
  // BorderLayout is used to place a panel at the top of the frame;
  // GridBagLayout is used to lay out the components inside the panel.
  //
  
  setLayout(new BorderLayout());
  Panel controlPanel = new Panel();
  
  GridBagLayout gridbag = new GridBagLayout();
  GridBagConstraints constraints = new GridBagConstraints();
  constraints.weightx = 1.0;
  controlPanel.setLayout(gridbag);

  //
  // Lay out controls and add them to the control panel.
  //
  
  Button runButton = new Button("Run");
  gridbag.setConstraints(runButton, constraints);
  controlPanel.add(runButton);
  
  Button editButton = new Button("Edit");
  gridbag.setConstraints(editButton, constraints);
  controlPanel.add(editButton);
  controlPanel.add(new Label("Damping", Label.RIGHT));
  
  dampingSlider = new Scrollbar(Scrollbar.HORIZONTAL, 0, 1, 0,
    (int)(MAX_DAMPING_VALUE*dampingScale));
  
  dampingSlider.setUnitIncrement(1);  
  dampingSlider.setForeground(Color.black);
  dampingSlider.setBackground(Color.white);
  constraints.fill = GridBagConstraints.BOTH;
  constraints.gridwidth = 3;
  gridbag.setConstraints(dampingSlider, constraints);
  controlPanel.add(dampingSlider);
  controlPanel.add(new Label("Nonlinearity", Label.RIGHT));
  
  fpuSlider = new Scrollbar(Scrollbar.HORIZONTAL, 0, 1, 0,
    (int)(MAX_FPU_VALUE*fpuScale));
  
  fpuSlider.setUnitIncrement(1);
  fpuSlider.setForeground(Color.black);
  fpuSlider.setBackground(Color.white);
  gridbag.setConstraints(fpuSlider, constraints);
  controlPanel.add(fpuSlider);
  
  // Place control panel at top of frame.
  pane.add("North", controlPanel);  
}

/// <summary>
/// Start the main application thread.
/// 
/// This seems to be called automatically in an applet context, but must
/// be called explicitly in a frame context.
/// </summary>
public void
start()
{
  if (solutionThread == null)
  {
    solutionThread = new Thread(this);
    solutionThread.start();
  }
}

/// <summary>
/// Stop the main application thread.
/// </summary>
public void
stop()
{
  solutionThread = null;
  offImage = null;
  offGraphics = null;
}

/// <summary>
/// This implements the main thread action.
///
/// "When an object implementing interface Runnable is used to create a thread, starting the
/// thread causes the object's run method to be called in that separately executing thread. 
/// The general contract of the method run is that it may take any action whatsoever."
/// </summary>
public void
run()
{  
  while (solutionThread != null)
  {
    try { Thread.sleep(msRefreshDelay); } catch (InterruptedException e) {}
    repaint();
    IterateSolutionGrid();
  }
  
  solutionThread = null;
}

/// <summary>
/// Draw graphics content.
/// </summary>
public void
paint(
  Graphics g)
{  
  // In frame context: update() never seems to be called unless we call
  // it explicitly.  This is not needed in the applet context.	
  Graphics2D g2 = (Graphics2D)g;
  
  update(g2);
  
  if (offImage != null)
    g2.drawImage(offImage, 0, 0, null);
}

/// <summary>
/// The update() method is overridden to implement double buffering.
///
/// This seems to have different behaviors in the applet and frame
/// contexts.
/// </summary>
public void
update(
  Graphics2D g)
{
  int px, py, x, y;
  
  Dimension d = getSize();

  Container pane = getContentPane();
  Color bg = pane.getBackground(), fg = pane.getForeground();
  
  // Create the off-screen graphics context.
  if (offGraphics == null ||
      d.width != offDimension.width ||
      d.height != offDimension.height)
  {
    offDimension = d;
    offImage = createImage(d.width, d.height);
    offGraphics = (Graphics2D) offImage.getGraphics();
    offGraphics.setStroke(new BasicStroke(4));
  }
  
  // Erase the previous image.
  offGraphics.setColor(bg);
  offGraphics.fillRect(0, 0, d.width, d.height);
  offGraphics.setColor(fg);
  
  // Draw the next image.
  px = xDisplayOffset;
  py = (int)(Y[0][currentStep]*yDisplayScale + yDisplayOffset);
  for(int i = 1; i < GRID_LENGTH; i++)
  {
    x = (int)(i*xDisplayScale) + xDisplayOffset;
    y = (int)(Y[i][currentStep]*yDisplayScale + yDisplayOffset);
    // offGraphics.drawLine(px, py, x, y);
    offGraphics.draw(new Line2D.Float(px, py, x, y));
    px = x;
    py = y;
  }
  
  // Paint the image on the screen.
  g.drawImage(offImage, 0, 0, null);
}

/// <summary>
/// DEPRECATED
///
/// This is called when mouse is dragged, allowing user to adjust string shape
/// whether or not string is in motion
/// </summary>
public boolean
mouseDrag(
  Event evt,
  int x,
  int y)
{
  System.out.println("mouseDrag()");
  
  int xStringPosition = (int)((x - xDisplayOffset)/xDisplayScale);
  double yStringPosition = (y - yDisplayOffset)/yDisplayScale;
  
  if (0 < xStringPosition && xStringPosition < GRID_LENGTH - 1)
    Y[xStringPosition][previousStep] = Y[xStringPosition][currentStep] = yStringPosition;
  
  if (stringStatus == STATUS_EDITING)
    repaint();
  
  return true;
}

/// <summary>
/// DEPRECATED
///
/// This is called when mouse exits applet space.
/// </summary>
public boolean
mouseExit(
  Event evt,
  int x,
  int y)
{
  showStatus("");
  return true;
}

/// <summary>
/// DEPRECATED
///
/// This is called when mouse enters applet space.
/// </summary>
public boolean
mouseEnter(
  Event evt,
  int x,
  int y)
{
  String strDimensions = "(" + framePixelWidth + "x" + framePixelHeight +")";
  showStatus(statusText[stringStatus] + " " + strDimensions);
  return true;
}

/// <summary>
/// This is an event handler to respond to user's actions on buttons
/// </summary>
public boolean
action(
  Event evt,
  Object arg)
{  
  if (evt.target instanceof Button)
  {
    String command = (String)arg;
    if (command.equals("Run"))
    {
      if (stringStatus != STATUS_RUNNING)
      {
    	stringStatus = STATUS_RUNNING;
    	solutionThread.resume();
      }
    }
    else if (command.equals("Edit"))
    {
      if (stringStatus != STATUS_EDITING)
      {
    	stringStatus = STATUS_EDITING;
    	solutionThread.suspend();
      }
    }
  }
  
  showStatus(statusText[stringStatus]);
  return true;
}

/// <summary>
/// This is a low-level event handler to respond to user's actions on sliders.
/// </summary>
public boolean
handleEvent(
  Event evt)
{
  if (evt.target instanceof Scrollbar)
    if (evt.target.equals(dampingSlider))
      dampingParameter = dampingSlider.getValue()/dampingScale;
    else if (evt.target.equals(fpuSlider))
      fpuParameter = fpuSlider.getValue()/fpuScale;
  
  return super.handleEvent(evt);
}

/// <summary>
/// Resize display window and recompute associated parameters.
/// </summary>
public void
setSize(
  int newWindowWidth,
  int newWindowHeight)
{
  framePixelWidth = newWindowWidth;
  framePixelHeight = newWindowHeight;
  xDisplayScale = framePixelWidth/(double)GRID_LENGTH;
  yDisplayScale = framePixelHeight/5.4;
  xDisplayOffset = (int)(0.5 + xDisplayScale/2);
  yDisplayOffset = framePixelHeight/2;
  
  showStatus("Frame resized to " + newWindowWidth + "x" + newWindowHeight);
  
  // Resize the actual window ('resize' is deprecated and replaced by 'setSize')
  super.setSize(framePixelWidth, framePixelHeight);
  validate();
}

/// <summary>
/// These abstract event-handler methods must be implemented.
/// </summary>
public void windowActivated(WindowEvent e) { }
public void windowClosed(WindowEvent e) { }
public void windowClosing(WindowEvent e) { }
public void windowDeactivated(WindowEvent e) { }
public void windowDeiconified(WindowEvent e) { }
public void windowIconified(WindowEvent e) { }
public void windowOpened(WindowEvent e) { }

/// <summary>
/// Display a window stringStatus message.
/// </summary>
public void
showStatus(
  String strMessage)
{
  System.out.println("New stringStatus: " + strMessage);
  // TODO: Emulate applet showStatus() method.	
}

/// <summary>
/// This is used by the new Swing event-handling framework
/// for the MouseListener instance.
/// </summary>
public void
mouseMoved(
  MouseEvent me)
{
}

/// <summary>
/// This is used by the new Swing event-handling framework
/// for the MouseListener instance.
/// </summary>
public void
mouseDragged(
  MouseEvent me)
{
  Point point = me.getPoint();
  int x = point.x, y = point.y;
    
  int i = (int)((x - xDisplayOffset)/xDisplayScale);
  
  double a = (y - yDisplayOffset)/yDisplayScale;  
  if (0 < i && i < GRID_LENGTH - 1)
    Y[i][previousStep] = Y[i][currentStep] = a;
  
  if (stringStatus == STATUS_EDITING)
    repaint();
}

/// <summary>
/// Handle resizing of animation window.
/// </summary>
public void
frameResized(
  ComponentEvent e)
{
  Dimension newSize = getSize();
  setSize(newSize.width, newSize.height);
}

/// <summary>
/// Main constructor.
/// </summary>
VibratingStringFrame()
{
  InitializeUserInterface();
  setVisible(true);  
}


/// <summary>
/// Program entry point.
/// </summary>
public static void
main(
  String args[])
{
  System.out.println("Starting vibrating-string simulation...");
  VibratingStringFrame frame = new VibratingStringFrame();

  // Add handlers for mouse events.
  frame.addMouseMotionListener(new MouseMotionAdapter()
  {
    // This is invoked when mouse is moved over the panel.
	public void mouseMoved(MouseEvent me)
	{
      frame.mouseMoved(me);
	}
	
	// This is invoked when mouse is dragged.
	public void mouseDragged(MouseEvent me)
	{
	  frame.mouseDragged(me);
	}
  });
  
  frame.addWindowListener(new WindowAdapter()
  {
    public void windowClosing(WindowEvent we)
    {
      System.exit(0);
    }
  });
  
  // Add handlers for window events.
  frame.addComponentListener(new ComponentListener()
  {
	// This is invoked when the frame is resized.
	public void componentResized(ComponentEvent e)
	{
	  // System.out.println("Component resized");
	  frame.frameResized(e);
	}
	
	// Other abstract methods must also be implemented.
	public void componentHidden(ComponentEvent e)
	{
	  // System.out.println("Component hidden");
	}
	
	public void componentShown(ComponentEvent e)
	{
	  // System.out.println("Component shown");
	}
	
	public void componentMoved(ComponentEvent e)
	{
	  // System.out.println("Component moved");
	}
  });
  
  // Start the animation.
  frame.start();
}

}

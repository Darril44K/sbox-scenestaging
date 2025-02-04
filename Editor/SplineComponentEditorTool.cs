using System;

public class SplineEditorTool : EditorTool<Spline>
{

	public override void OnEnabled()
	{
		window = new SplineToolWindow();
		AddOverlay( window, TextFlag.RightBottom, 10 );
	}

	public override void OnUpdate()
	{
		window.ToolUpdate();
	}

	public override void OnDisabled()
	{
		window.OnDisabled();
	}

	public override void OnSelectionChanged()
	{
		var target = GetSelectedComponent<Spline>();
		window.OnSelectionChanged( target );
	}

	private SplineToolWindow window = null;
}

class SplineToolWindow : WidgetWindow
{
	Spline targetComponent;

	static bool IsClosed = false;

	ControlWidget inTangentControl;
	ControlWidget outTangentControl;

	public SplineToolWindow()
	{
		ContentMargins = 0;
		Layout = Layout.Column();
		MaximumWidth = 500;
		MinimumWidth = 300;

		Rebuild();
	}

	void Rebuild()
	{
		Layout.Clear( true );
		Layout.Margin = 0;
		Icon = IsClosed ? "" : "route";
		UpdateWindowTitle();
		IsGrabbable = !IsClosed;

		if ( IsClosed )
		{
			var closedRow = Layout.AddRow();
			closedRow.Add( new IconButton( "route", () => { IsClosed = false; Rebuild(); } ) { ToolTip = "Open Spline Point Editor", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Color.Transparent } );
			MinimumWidth = 0;
			return;
		}

		MinimumWidth = 400;

		var headerRow = Layout.AddRow();
		headerRow.AddStretchCell();
		headerRow.Add( new IconButton( "info" )
		{
			ToolTip = "Controls to edit the spline points.\nIn addition to modifying the properties in the control sheet, you can also use the 3D Gizmos.\nClicking on the spline between points will split the spline at that position.\nHolding shift while dragging a point's position will drag out a new point.",
			FixedHeight = HeaderHeight,
			FixedWidth = HeaderHeight,
			Background = Color.Transparent
		} );
		headerRow.Add( new IconButton( "close", CloseWindow ) { ToolTip = "Close Editor", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Color.Transparent } );

		if ( targetComponent.IsValid() )
		{
			this.GetSerialized().GetProperty( nameof ( _selectedPoint ) ).TryGetAsObject( out SerializedObject point );
			var tangentMode = this.GetSerialized().GetProperty( nameof( _selectedPointTangentMode ) );
			var roll = this.GetSerialized().GetProperty( nameof( _selectedPointRoll ) );
			var scale = this.GetSerialized().GetProperty( nameof( _selectedPointScale ) );
			var up = this.GetSerialized().GetProperty( nameof( _selectedPointUp ) );

			var controlSheet = new ControlSheet();

			controlSheet.AddRow( tangentMode );
			controlSheet.AddRow( point.GetProperty( nameof( SplinePoint.Position ) ) );
			inTangentControl = controlSheet.AddRow( point.GetProperty( nameof( SplinePoint.InPositionRelative ) ) );
			outTangentControl = controlSheet.AddRow( point.GetProperty( nameof( SplinePoint.OutPositionRelative ) ) );
			controlSheet.AddGroup( "Advanced", new[] { roll, scale, up } );

			var row = Layout.Row();
			row.Spacing = 16;
			row.Margin = 8;
			row.Add( new IconButton( "skip_previous", () =>
			{
				SelectedPointIndex = Math.Max( 0, SelectedPointIndex - 1 );

				UpdateWindowTitle();
				Focus();
			} )
			{ ToolTip = "Go to previous point " } );
			row.Add( new IconButton( "skip_next", () =>
			{
				SelectedPointIndex = Math.Min( targetComponent.NumberOfPoints() - 1, SelectedPointIndex + 1 );

				UpdateWindowTitle();
				Focus();
			} )
			{ ToolTip = "Go to next point" } );
			row.Add( new IconButton( "delete", () =>
			{
				targetComponent.RemovePoint( SelectedPointIndex );
				SelectedPointIndex = Math.Max( 0, SelectedPointIndex - 1 );
				targetComponent.EditLog( "Deleted point", targetComponent );

				UpdateWindowTitle();
				Focus();
			} )
			{ ToolTip = "Delete point" } );
			row.Add( new IconButton( "add", () =>
			{
				if ( SelectedPointIndex == targetComponent.NumberOfPoints() - 1 )
				{
					targetComponent.InsertPoint( SelectedPointIndex + 1, _selectedPoint with { Position = _selectedPoint.Position + targetComponent.GetTangetAtDistance( targetComponent.GetDistanceAtPoint( SelectedPointIndex ) ) * 200 } );
				}
				else
				{
					targetComponent.AddPointAtDistance( (targetComponent.GetDistanceAtPoint( SelectedPointIndex ) + targetComponent.GetDistanceAtPoint( SelectedPointIndex + 1 )) / 2, true );
					// TOOD infer tangent modes???
				}
				targetComponent.EditLog( "Added spline point", targetComponent );

				SelectedPointIndex++;

				UpdateWindowTitle();
				Focus();
			} )
			{ ToolTip = "Insert point after curent point.\nYou can also hold shift while dragging a point to create a new point." } );

			controlSheet.AddLayout( row );

			Layout.Add( controlSheet );

			ToggleTangentInput();
		}


		Layout.Margin = 4;
	}

	void UpdateWindowTitle()
	{
		WindowTitle = IsClosed ? "" : $"Spline Point [{SelectedPointIndex}] Editor - {targetComponent?.GameObject?.Name ?? ""}";
	}

	void CloseWindow()
	{
		IsClosed = true;
		// TODO internal ?
		// Release();
		Rebuild();
		Position = Parent.Size - 32;
	}

	public void ToolUpdate()
	{
		if ( !targetComponent.IsValid() )
			return;

		DrawGizmos();
	}

	public void OnSelectionChanged( Spline spline )
	{
		if ( targetComponent.IsValid() )
		{
			targetComponent.ShouldRenderGizmos = true;
		}
		
		targetComponent = spline;

		targetComponent.ShouldRenderGizmos = false;

		Rebuild();
	}

	public void OnDisabled()
	{
		if ( targetComponent.IsValid() )
		{
			targetComponent.ShouldRenderGizmos = true;
		}
	}

	private void ToggleTangentInput()
	{
		if ( targetComponent.GetTangentModeForPoint( _selectedPointIndex ) == SplinePointTangentMode.Auto || targetComponent.GetTangentModeForPoint( _selectedPointIndex ) == SplinePointTangentMode.Linear )
		{
			inTangentControl.Enabled = false;
			outTangentControl.Enabled = false;
		}
		else
		{
			inTangentControl.Enabled = true;
			outTangentControl.Enabled = true;
		}
	}

	int SelectedPointIndex
	{
		get => _selectedPointIndex;
		set
		{
			_selectedPointIndex = value;
			ToggleTangentInput();
		}
	}

	int _selectedPointIndex = 0;

	SplinePoint _selectedPoint
	{
		get => SelectedPointIndex > targetComponent.NumberOfPoints() - 1 ? new SplinePoint() : targetComponent.GetPoint( SelectedPointIndex );
		set
		{
			targetComponent.UpdatePoint( SelectedPointIndex, value );
			targetComponent.EditLog( "Updated spline point", targetComponent );
		}
	}

	[Title( "Tangent Mode" )]
	SplinePointTangentMode _selectedPointTangentMode
	{
		get => SelectedPointIndex > targetComponent.NumberOfPoints() - 1 ? SplinePointTangentMode.Auto : targetComponent.GetTangentModeForPoint( SelectedPointIndex );
		set
		{
			targetComponent.SetTangentModeForPoint( SelectedPointIndex, value );
			targetComponent.EditLog( "Updated spline point", targetComponent );
			ToggleTangentInput();
		}
	}

	[Title( "Roll (Degrees)" )]
	float _selectedPointRoll
	{
		get => SelectedPointIndex > targetComponent.NumberOfPoints() - 1 ? 0f : targetComponent.GetRollForPoint( SelectedPointIndex );
		set
		{
			targetComponent.SetRollForPoint( SelectedPointIndex, value );
			targetComponent.EditLog( "Updated spline point", targetComponent );
		}
	}

	[Title( "Up Vector" )]
	Vector3 _selectedPointUp
	{
		get => SelectedPointIndex > targetComponent.NumberOfPoints() - 1 ? Vector3.Zero : targetComponent.GetUpVectorForPoint( SelectedPointIndex );
		set
		{
			targetComponent.SetUpVectorForPoint( SelectedPointIndex, value );
			targetComponent.EditLog( "Updated spline point", targetComponent );
		}
	}

	[Title( "Scale (Width, Height)" )]
	Vector2 _selectedPointScale
	{
		get => SelectedPointIndex > targetComponent.NumberOfPoints() - 1 ? 0f : targetComponent.GetScaleForPoint( SelectedPointIndex );
		set
		{
			targetComponent.SetScaleForPoint( SelectedPointIndex, value );
			targetComponent.EditLog( "Updated spline point", targetComponent );
		}
	}

	bool _inTangentSelected = false;

	bool _outTangentSelected = false;

	bool _draggingOutNewPoint = false;

	bool _moveInProgress = false;

	List<Vector3> polyLine = new();

	void DrawGizmos()
	{

		using ( Gizmo.Scope( "spline_editor", targetComponent.WorldTransform ) )
		{
			targetComponent.ConvertToPolyline( ref polyLine );

			for ( var i = 0; i < polyLine.Count - 1; i++ )
			{
				using ( Gizmo.Scope( "segment" + i ) )
				{
					using ( Gizmo.Hitbox.LineScope() )
					{
						Gizmo.Draw.LineThickness = 2f;

						Gizmo.Hitbox.AddPotentialLine( polyLine[i], polyLine[i + 1], Gizmo.Draw.LineThickness * 2f );

						Gizmo.Draw.Line( polyLine[i], polyLine[i + 1] );

						if ( Gizmo.IsHovered && Gizmo.HasMouseFocus )
						{
							Gizmo.Draw.Color = Color.Orange;
							Vector3 point_on_line;
							Vector3 point_on_ray;
							if ( !new Line( polyLine[i], polyLine[i + 1] ).ClosestPoint(
									Gizmo.CurrentRay.ToLocal( Gizmo.Transform ), out point_on_line, out point_on_ray ) )
								return;

							// It would be slighlty more efficient to use Spline.Utils directly,
							// but doggfoding the simplified component API ensures a user of that one would also have the ability to built a spline editor
							var hoverDistance = targetComponent.FindDistanceClosestToPosition( point_on_line );

							using ( Gizmo.Scope( "hover_handle", new Transform( point_on_line,
									   Rotation.LookAt( targetComponent.GetTangetAtDistance( hoverDistance ) ) ) ) )
							{
								using ( Gizmo.GizmoControls.PushFixedScale() )
								{
									Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( Vector3.Zero, 2f ) );
								}
							}

							if ( Gizmo.HasClicked && Gizmo.Pressed.This )
							{
								var newPointIndex = targetComponent.AddPointAtDistance( hoverDistance, true );
								SelectedPointIndex = newPointIndex;
								_inTangentSelected = false;
								_outTangentSelected = false;
								targetComponent.EditLog( "Added spline point", targetComponent );
							}
						}
					}
				}
			}

			// position location
			var positionGizmoLocation = _selectedPoint.Position;
			if ( _inTangentSelected )
			{
				positionGizmoLocation = _selectedPoint.InPosition;
			}

			if ( _outTangentSelected )
			{
				positionGizmoLocation = _selectedPoint.OutPosition;
			}

			if ( !Gizmo.IsShiftPressed )
			{
				_draggingOutNewPoint = false;
			}

			using ( Gizmo.Scope( "position", new Transform( positionGizmoLocation ) ) )
			{
				_moveInProgress = false;
				if ( Gizmo.Control.Position( "spline_control_", Vector3.Zero, out var delta ) )
				{
					_moveInProgress = true;
					if ( _inTangentSelected )
					{
						MoveSelectedPointInTanget( delta );
					}
					else if ( _outTangentSelected )
					{
						MoveSelectedPointOutTanget( delta );
					}
					else
					{
						if ( Gizmo.IsShiftPressed && !_draggingOutNewPoint )
						{
							_draggingOutNewPoint = true;
							var newPointTangentMode = targetComponent.GetTangentModeForPoint( SelectedPointIndex );
							var newpointRoll = targetComponent.GetRollForPoint( SelectedPointIndex );
							var newPointScale = targetComponent.GetScaleForPoint( SelectedPointIndex );

							targetComponent.InsertPoint( SelectedPointIndex + 1, _selectedPoint );
							targetComponent.SetTangentModeForPoint( SelectedPointIndex + 1, newPointTangentMode );
							targetComponent.SetRollForPoint( SelectedPointIndex + 1, newpointRoll );
							targetComponent.SetScaleForPoint( SelectedPointIndex + 1, newPointScale );

							SelectedPointIndex++;
						}
						else
						{
							MoveSelectedPoint( delta );
						}
					}
				}
				if ( !_moveInProgress && Gizmo.WasLeftMouseReleased )
				{
					targetComponent.EditLog( "Moved spline point", targetComponent );
				}
			}

			for ( var i = 0; i < targetComponent.NumberOfPoints(); i++ )
			{
				if ( !targetComponent.IsLoop || i != targetComponent.NumberOfSegments() )
				{
					var splinePoint = targetComponent.GetPoint( i );

					using ( Gizmo.Scope( "point_controls" + i, new Transform( splinePoint.Position ) ) )
					{
						Gizmo.Draw.IgnoreDepth = true;

						using ( Gizmo.Scope( "position" ) )
						{
							using ( Gizmo.GizmoControls.PushFixedScale() )
							{
								Gizmo.Hitbox.DepthBias = 0.1f;
								Gizmo.Hitbox.BBox( BBox.FromPositionAndSize( Vector3.Zero, 2f ) );

								if ( Gizmo.IsHovered || i == SelectedPointIndex &&
									(!_inTangentSelected && !_outTangentSelected) )
								{
									Gizmo.Draw.Color = Color.Orange;
								}

								Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( Vector3.Zero, 2f ) );

								if ( Gizmo.HasClicked && Gizmo.Pressed.This )
								{
									SelectedPointIndex = i;
									_inTangentSelected = false;
									_outTangentSelected = false;
								}
							}
						}

						Gizmo.Draw.Color = Color.White;


						if ( SelectedPointIndex == i )
						{
							Gizmo.Draw.LineThickness = 0.8f;
							using ( Gizmo.Scope( "in_tangent", new Transform( splinePoint.InPositionRelative ) ) )
							{

								if ( (_selectedPointTangentMode == SplinePointTangentMode.Mirrored || _selectedPointTangentMode == SplinePointTangentMode.Auto) && (_inTangentSelected || _outTangentSelected) )
								{
									Gizmo.Draw.Color = Color.Orange;
								}

								Gizmo.Draw.Line( -splinePoint.InPositionRelative, Vector3.Zero );

								using ( Gizmo.GizmoControls.PushFixedScale() )
								{
									if ( _selectedPointTangentMode != SplinePointTangentMode.Linear )
									{
										Gizmo.Hitbox.DepthBias = 0.1f;
										Gizmo.Hitbox.BBox( BBox.FromPositionAndSize( Vector3.Zero, 2f ) );

										if ( Gizmo.IsHovered || _inTangentSelected )
										{
											Gizmo.Draw.Color = Color.Orange;
										}

										Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( Vector3.Zero, 2f ) );

										if ( Gizmo.HasClicked && Gizmo.Pressed.This )
										{
											SelectedPointIndex = i;
											_outTangentSelected = false;
											_inTangentSelected = true;
										}
									}

								}
							}

							using ( Gizmo.Scope( "out_tangent", new Transform( splinePoint.OutPositionRelative ) ) )
							{
								if ( (_selectedPointTangentMode == SplinePointTangentMode.Mirrored || _selectedPointTangentMode == SplinePointTangentMode.Auto) && (_inTangentSelected || _outTangentSelected) )
								{
									Gizmo.Draw.Color = Color.Orange;
								}

								Gizmo.Draw.Line( -splinePoint.OutPositionRelative, Vector3.Zero );

								using ( Gizmo.GizmoControls.PushFixedScale() )
								{
									if ( _selectedPointTangentMode != SplinePointTangentMode.Linear )
									{

										Gizmo.Hitbox.BBox( BBox.FromPositionAndSize( Vector3.Zero, 2f ) );

										if ( Gizmo.IsHovered || _outTangentSelected )
										{
											Gizmo.Draw.Color = Color.Orange;
										}

										Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( Vector3.Zero, 2f ) );

										if ( Gizmo.HasClicked && Gizmo.Pressed.This )
										{
											SelectedPointIndex = i;
											_inTangentSelected = false;
											_outTangentSelected = true;
										}

									}
								}
							}
						}
					}
				}
			}
		}
	}

	private void MoveSelectedPoint( Vector3 delta )
	{
		var updatedPoint = _selectedPoint with { Position = _selectedPoint.Position + delta };
		targetComponent.UpdatePoint( SelectedPointIndex, updatedPoint );
	}

	private void MoveSelectedPointInTanget( Vector3 delta )
	{
		var updatedPoint = _selectedPoint;
		updatedPoint.InPositionRelative += delta;
		if ( _selectedPointTangentMode == SplinePointTangentMode.Auto )
		{
			targetComponent.SetTangentModeForPoint( SelectedPointIndex, SplinePointTangentMode.Mirrored );
		}
		if ( _selectedPointTangentMode == SplinePointTangentMode.Mirrored )
		{
			updatedPoint.OutPositionRelative = -updatedPoint.InPositionRelative;
		}
		targetComponent.UpdatePoint( SelectedPointIndex, updatedPoint );
	}

	private void MoveSelectedPointOutTanget( Vector3 delta )
	{
		var updatedPoint = _selectedPoint;
		updatedPoint.OutPositionRelative += delta;
		if ( _selectedPointTangentMode == SplinePointTangentMode.Auto )
		{
			targetComponent.SetTangentModeForPoint( SelectedPointIndex, SplinePointTangentMode.Mirrored );
		}
		if ( _selectedPointTangentMode == SplinePointTangentMode.Mirrored )
		{
			updatedPoint.InPositionRelative = -updatedPoint.OutPositionRelative;
		}
		targetComponent.UpdatePoint( SelectedPointIndex, updatedPoint );
	}
}

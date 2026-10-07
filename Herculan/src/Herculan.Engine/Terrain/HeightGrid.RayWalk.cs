using Herculan.Engine.Numerics;

namespace Herculan.Engine.Terrain;

/// <summary>
/// The ray-versus-terrain query — <c>Terrain_RayWalk</c> (<c>0046e87c</c>), the terrain
/// module's largest function, plus the two helpers it resolves a hit with. This is what stops a
/// shot passing through a hillside: <c>Sim_RaycastTerrain</c> (<c>00428048</c>) runs it before
/// <c>Sim_RaycastObjectList</c> tests a single object, and clips the ray to the ground hit.
///
/// <para>The original takes a mode flag as its last argument, and both are here. Mode 0, the thin
/// ray, reports the ground wherever the segment is at or below the surface. Mode 1
/// (<see cref="RayWalkVolume"/>) instead asks whether the face the segment is crossing is one
/// movement can pass — a <b>slope test</b>, so a segment sliding along rolling ground reports
/// nothing and only a wall stops it. See docs/retail/formats/terrain-heightmap.md ("Mode 1 — the slope
/// walk").</para>
///
/// <para>The setup — the delta clamp, the four slopes, the octant code and the cell stepping — is
/// shared; only the per-step surface test differs.</para>
/// </summary>
public sealed partial class HeightGrid {
	/// <summary>
	/// Walks the grid from <paramref name="start"/> to <paramref name="end"/> and reports the first
	/// point at which the segment passes into the ground.
	///
	/// <para>The walk is a DDA over cells. Each step produces the point where the segment leaves the
	/// current cell and which of the cell's four edges it leaves through; the edge's two corner
	/// heights say whether the segment is already below the surface there
	/// (<see cref="ExitBelowSurface"/>). The last step — the one that ends at
	/// <paramref name="end"/> rather than at a cell boundary — has no exit edge and falls back to a
	/// plain height query at each end of it instead. Only once a step reports a crossing is the
	/// exact point solved for, by intersecting the segment with the cell's own triangle planes
	/// (<see cref="SurfaceIntersection"/>).</para>
	///
	/// <para>Note what the very first iteration adds: it also height-queries the <i>start</i> point,
	/// so a ray that begins underground is a hit immediately rather than one that has to reach a
	/// boundary first.</para>
	///
	/// <para>Returns false — no hit — for a segment starting outside the grid, or one that walks off
	/// its edge, exactly as the original does.</para>
	/// </summary>
	/// <param name="hitPoint">Where the segment met the ground; only meaningful when this returns true.</param>
	public bool RayWalk(Vec3i start, Vec3i end, out Vec3i hitPoint) =>
		Walk(start, end, volume: false, out hitPoint);

	/// <summary>
	/// <c>Terrain_RayWalk</c>'s mode 1, the slope walk (<c>Terrain_FaceBlocksAt</c>
	/// (<c>0046fe84</c>) / <c>Terrain_EdgeFaceBlocks</c> (<c>0046ff74</c>) /
	/// <c>Terrain_FaceBlocksMovement</c> (<c>0046fe40</c>)). The same walk, with the "is this point
	/// under the ground" test replaced by "is the face this step crosses too steep to walk", so it
	/// answers what blocks a machine rather than what a bullet would hit. Its two callers are the AI's
	/// obstacle probes and its line-of-sight test.
	/// </summary>
	/// <param name="hitPoint">
	/// Where the segment met the blocking face, its <c>z</c> 0 when the point came from
	/// <see cref="DiagonalCrossing"/>. Only the obstacle probes read it, and only its <c>x</c> and
	/// <c>y</c>.
	/// </param>
	public bool RayWalkVolume(Vec3i start, Vec3i end, out Vec3i hitPoint) =>
		Walk(start, end, volume: true, out hitPoint);

	private bool Walk(Vec3i start, Vec3i end, bool volume, out Vec3i hitPoint) {
		hitPoint = default;

		// Halve the delta until every component fits a signed short's worth of magnitude. Only the
		// ratios below are taken from it, so scaling all three together changes nothing; the clamp
		// exists because the volume mode packs the delta into three shorts.
		int deltaX = end.X - start.X;
		int deltaY = end.Y - start.Y;
		int deltaZ = end.Z - start.Z;
		while (deltaX > 32000 || deltaX < -32000 || deltaY > 32000 || deltaY < -32000
				|| deltaZ > 32000 || deltaZ < -32000) {
			deltaX >>= 1;
			deltaY >>= 1;
			deltaZ >>= 1;
		}

		// Mode 1 tests faces against the segment's own direction, packed into three shorts and scaled
		// to a normal's length by the same normaliser the face normals went through, so the dot
		// product below is in their units.
		int dirX = 0, dirY = 0, dirZ = 0;
		if (volume) {
			(dirX, dirY, dirZ) = Normalize((short)deltaX, (short)deltaY, (short)deltaZ);
		}

		// The four slopes, in Q16: two per unit of X travelled and two per unit of Y. A zero
		// denominator takes the original's literal 1.0 rather than a guard — the octant below makes
		// sure the pair belonging to a zero axis is never the one used.
		int slopeYPerX = deltaX == 0 ? 0x10000 : SimMath.Q16Divide(deltaY, deltaX);
		int slopeZPerX = deltaX == 0 ? 0x10000 : SimMath.Q16Divide(deltaZ, deltaX);
		int slopeXPerY = deltaY == 0 ? 0x10000 : SimMath.Q16Divide(deltaX, deltaY);
		int slopeZPerY = deltaY == 0 ? 0x10000 : SimMath.Q16Divide(deltaZ, deltaY);

		int octant = Octant(deltaX, deltaY);

		// Which axis the walk steps along by default (the major one), which way each axis runs, and
		// the edge code a crossing of each reports.
		bool majorIsX = octant is 0 or 3 or 4 or 7;
		int stepX = octant is 0 or 1 or 6 or 7 ? 1 : -1;
		int stepY = octant is 0 or 1 or 2 or 3 ? 1 : -1;
		int edgeX = stepX > 0 ? EdgeEast : EdgeWest;
		int edgeY = stepY > 0 ? EdgeNorth : EdgeSouth;

		// Six of this function's multiplies are compiled as a plain 32-bit imul/sar pair rather than
		// through Math_Q16Multiply, so they truncate where the rest do not. All six sit in the
		// octant-2 and octant-3 arms. It only diverges at grazing angles, where a slope is large
		// enough for the product to leave 32 bits, but it is the original's own arithmetic and is
		// reproduced rather than tidied up.
		bool truncateMajor = octant == 3;
		bool truncateMinor = octant is 2 or 3;

		int cellSize = 1 << CellShift;
		int cellX = start.X >> CellShift;
		int cellY = start.Y >> CellShift;
		if (cellX < 0 || cellX >= Width || cellY < 0 || cellY >= Height) {
			return false;
		}

		// The next cell boundary on each axis, in world units: the far edge of the starting cell in
		// whichever direction that axis runs.
		int boundX = (cellX << CellShift) + (stepX > 0 ? cellSize : 0);
		int boundY = (cellY << CellShift) + (stepY > 0 ? cellSize : 0);

		// The cell the current step is tested against, and the one the step has advanced into. They
		// are one apart for the length of an iteration and re-synced at the bottom of it.
		int nextCellX = cellX;
		int nextCellY = cellY;

		int curX = start.X, curY = start.Y, curZ = start.Z;
		int exitX = 0, exitY = 0, exitZ = 0;
		int savedX = 0, savedY = 0, savedZ = 0;
		int carryMinor = 0, carryZ = 0;

		bool secondPass = false;
		bool reachedEnd = false;
		bool first = true;
		bool lastStep;

		do {
			if (cellX < 0 || cellX >= Width || cellY < 0 || cellY >= Height) {
				return false;
			}

			int edge;
			if (secondPass) {
				// The step before this one was interrupted by a minor-axis crossing; pick the
				// major-axis exit point back up from where it was stashed.
				secondPass = false;
				exitX = savedX;
				exitY = savedY;
				exitZ = savedZ;
				edge = majorIsX ? edgeX : edgeY;
				if (majorIsX) {
					nextCellX += stepX;
					boundX += stepX * cellSize;
				} else {
					nextCellY += stepY;
					boundY += stepY * cellSize;
				}
			} else {
				// Where the segment leaves this cell across the major axis — or the segment's own
				// end, if that comes first.
				if (majorIsX
						? (stepX > 0 ? boundX < end.X : end.X < boundX)
						: (stepY > 0 ? boundY < end.Y : end.Y < boundY)) {
					if (first) {
						// The first step has to solve for its exit point outright. Every one after
						// it spans a whole cell along the major axis, so the minor and Z advances
						// are constant from here and get carried instead.
						if (majorIsX) {
							int run = boundX - curX;
							exitX = boundX;
							exitY = Scale(run, slopeYPerX, truncateMajor) + curY;
							exitZ = Scale(run, slopeZPerX, truncateMajor) + curZ;
							carryMinor = stepX * SimMath.Q16Multiply(cellSize, slopeYPerX);
							carryZ = stepX * SimMath.Q16Multiply(cellSize, slopeZPerX);
						} else {
							int run = boundY - curY;
							exitY = boundY;
							exitX = Scale(run, slopeXPerY, truncateMajor) + curX;
							exitZ = Scale(run, slopeZPerY, truncateMajor) + curZ;
							carryMinor = stepY * SimMath.Q16Multiply(cellSize, slopeXPerY);
							carryZ = stepY * SimMath.Q16Multiply(cellSize, slopeZPerY);
						}
					} else if (majorIsX) {
						exitX = boundX;
						exitY = curY + carryMinor;
						exitZ = curZ + carryZ;
					} else {
						exitY = boundY;
						exitX = curX + carryMinor;
						exitZ = curZ + carryZ;
					}
				} else {
					reachedEnd = true;
					exitX = end.X;
					exitY = end.Y;
					exitZ = end.Z;
				}

				// A minor-axis boundary can still fall between here and that exit point, in which
				// case the cell is left across the minor edge first and the major exit is stashed
				// for the following iteration.
				bool minorFirst = majorIsX
					? (stepY > 0 ? boundY < exitY : exitY < boundY)
					: (stepX > 0 ? boundX < exitX : exitX < boundX);

				if (minorFirst) {
					savedX = exitX;
					savedY = exitY;
					savedZ = exitZ;
					secondPass = true;

					if (majorIsX) {
						int run = boundY - curY;
						exitY = boundY;
						exitX = Scale(run, slopeXPerY, truncateMinor) + curX;
						exitZ = Scale(run, slopeZPerY, truncateMinor) + curZ;
						edge = edgeY;
						nextCellY += stepY;
						boundY += stepY * cellSize;
					} else {
						int run = boundX - curX;
						exitX = boundX;
						exitY = Scale(run, slopeYPerX, truncateMinor) + curY;
						exitZ = Scale(run, slopeZPerX, truncateMinor) + curZ;
						edge = edgeX;
						nextCellX += stepX;
						boundX += stepX * cellSize;
					}
				} else if (majorIsX) {
					edge = edgeX;
					nextCellX += stepX;
					boundX += stepX * cellSize;
				} else {
					edge = edgeY;
					nextCellY += stepY;
					boundY += stepY * cellSize;
				}
			}

			// This step runs to the segment's end rather than to a cell edge only if the major exit
			// was clamped to it *and* no minor crossing displaced it.
			lastStep = reachedEnd && !secondPass;

			if (volume) {
				// The segment's own start is tested outright, ahead of the step, and only on the first
				// iteration — the original clears its flag at the bottom of the loop just as `first` is
				// cleared here.
				if (first && FaceBlocksAt(curX, curY, dirX, dirY, dirZ)) {
					hitPoint = new Vec3i(curX, curY, curZ);
					return true;
				}

				// A face in this cell reports where the step entered the triangle it ends in; one across
				// the exit edge reports the exit point.
				if (lastStep) {
					if (!FaceBlocksAt(exitX, exitY, dirX, dirY, dirZ)) {
						return false;
					}

					hitPoint = DiagonalCrossing(cellX, cellY, curX, curY, curZ, exitX, exitY);
					return true;
				}

				int blocked = EdgeFaceBlocks(cellX, cellY, edge, dirX, dirY, dirZ);
				if (blocked != 0) {
					hitPoint = blocked == BlockedThisCell
						? DiagonalCrossing(cellX, cellY, curX, curY, curZ, exitX, exitY)
						: new Vec3i(exitX, exitY, exitZ);
					return true;
				}
			} else if (!lastStep) {
				if (ExitBelowSurface(cellX, cellY, exitX, exitY, exitZ, edge)) {
					return Resolve(cellX, cellY, curX, curY, curZ, exitX, exitY, exitZ,
						exitX, exitY, exitZ, out hitPoint);
				}
			} else {
				if (exitZ <= HeightAtWorld(exitX, exitY)) {
					return Resolve(cellX, cellY, curX, curY, curZ, exitX, exitY, exitZ,
						exitX, exitY, exitZ, out hitPoint);
				}

				// Past the first step the segment's own start was already cleared by the step before.
				if (!first) {
					return false;
				}

				if (curZ <= HeightAtWorld(curX, curY)) {
					return Resolve(cellX, cellY, curX, curY, curZ, exitX, exitY, exitZ,
						curX, curY, curZ, out hitPoint);
				}
			}

			if (!lastStep) {
				curX = exitX;
				curY = exitY;
				curZ = exitZ;
				cellY = nextCellY;
				cellX = nextCellX;
			}

			first = false;
		} while (!lastStep);

		return false;
	}

	/// <summary>
	/// Reports the hit the walk has just found, refined to the cell's triangle planes.
	///
	/// <para>The fallback is a deviation, and the only one here. When the plane solve finds nothing
	/// the original returns "hit" with its output <b>left unwritten</b> — its caller then measures a
	/// distance to whatever was on the stack. Rather than reproduce reading uninitialised memory,
	/// the walk's own point for that step stands in. It is reachable only when a cell reports a
	/// crossing that neither of its triangles then confirms.</para>
	/// </summary>
	private bool Resolve(int cellX, int cellY, int fromX, int fromY, int fromZ, int toX, int toY, int toZ,
			int fallbackX, int fallbackY, int fallbackZ, out Vec3i hitPoint) {
		hitPoint = SurfaceIntersection(cellX, cellY, fromX, fromY, fromZ, toX, toY, toZ)
			?? new Vec3i(fallbackX, fallbackY, fallbackZ);
		return true;
	}

	/// <summary>The exit edge codes the walk reports and <see cref="ExitBelowSurface"/> reads.</summary>
	private const int EdgeWest = 0;
	private const int EdgeEast = 1;
	private const int EdgeNorth = 2;
	private const int EdgeSouth = 3;

	/// <summary>
	/// The original's direction code: which of eight 45° sectors the ground-plane delta falls in,
	/// which is what picks the major axis and both step directions in one value. Ties go to X being
	/// the major axis.
	/// </summary>
	private static int Octant(int deltaX, int deltaY) {
		if (deltaX < 0) {
			if (deltaY < 0) {
				return deltaX < deltaY ? 4 : 5;
			}

			return -deltaX < deltaY ? 2 : 3;
		}

		if (deltaY < 0) {
			return -deltaX == deltaY || -deltaY < deltaX ? 7 : 6;
		}

		return deltaY < deltaX ? 0 : 1;
	}

	/// <summary>
	/// A Q16 scale that is either the sim's usual 64-bit one or the truncating 32-bit form the
	/// original compiled into six of the walk's arms — see the note in <see cref="RayWalk"/>.
	/// </summary>
	private static int Scale(int value, int slope, bool truncate) =>
		truncate ? unchecked(value * slope) >> 16 : SimMath.Q16Multiply(value, slope);

	/// <summary>
	/// <c>Terrain_EdgeCrossingTest</c> (<c>0047035c</c>) — the per-step crossing test. Takes the point at which the segment leaves
	/// a cell and the edge it leaves through, and answers whether the terrain along that edge is at
	/// or above it.
	///
	/// <para>Cheap rejections first: if both of the edge's corners are below the exit point the
	/// segment is clear of it, and if both are at or above it the segment is already under the
	/// surface with nothing to interpolate. Only the straddling case works out the edge's height at
	/// the exit point itself.</para>
	///
	/// <para>The corner bounds test is the original's, which checks only the <i>flat</i> index — so
	/// an edge in the last column reads the next row's first cell as its east corner, the same wrap
	/// <see cref="HeightAtWorld"/> reproduces.</para>
	/// </summary>
	private bool ExitBelowSurface(int cellX, int cellY, int exitX, int exitY, int exitZ, int edge) {
		int nearCorner, farCorner;
		switch (edge) {
			case EdgeWest:
				nearCorner = CornerIndex(cellX, cellY);
				farCorner = CornerIndex(cellX, cellY + 1);
				break;
			case EdgeEast:
				nearCorner = CornerIndex(cellX + 1, cellY);
				farCorner = CornerIndex(cellX + 1, cellY + 1);
				break;
			case EdgeNorth:
				nearCorner = CornerIndex(cellX, cellY + 1);
				farCorner = CornerIndex(cellX + 1, cellY + 1);
				break;
			default:
				nearCorner = CornerIndex(cellX, cellY);
				farCorner = CornerIndex(cellX + 1, cellY);
				break;
		}

		if (nearCorner < 0 || farCorner < 0) {
			return false;
		}

		int nearHeight = _rawHeights[nearCorner] * HeightScale + HeightBase;
		int farHeight = _rawHeights[farCorner] * HeightScale + HeightBase;

		if (Math.Min(nearHeight, farHeight) >= exitZ) {
			return true;
		}

		if (Math.Max(nearHeight, farHeight) < exitZ) {
			return false;
		}

		// The west and east edges run along Y; the north and south edges run along X.
		int along = edge < EdgeNorth
			? exitY - (cellY << CellShift)
			: exitX - (cellX << CellShift);

		return exitZ <= nearHeight + ((along * (farHeight - nearHeight)) >> CellShift);
	}

	/// <summary>
	/// <c>Terrain_CellSurfaceIntersect</c> (<c>0047068c</c>) — where exactly the segment meets the ground inside one cell, solved
	/// against the cell's own two triangle planes rather than by interpolating along the walk.
	///
	/// <para>Each plane is one of the face normals <see cref="BuildSurface"/> already built, put
	/// through a corner that triangle contains. Which triangle a hit belongs to is decided by the
	/// same diagonal split <see cref="HeightAtWorld"/> uses, and the near triangle is tried first; a
	/// hit outside it falls through to the far one. For the anti-diagonal split (selector 0) the far
	/// triangle has no corner in common with this cell's own record, so the original shifts a cell
	/// east and takes that corner instead — which is why the two selectors are not symmetric
	/// here.</para>
	///
	/// <para>One quirk is reproduced deliberately. For the diagonal split (selector 2) the far
	/// plane's constant is <b>not</b> recomputed against the far normal: the original leaves the
	/// near triangle's value in place, so the far plane is skewed by the difference between the two
	/// normals' Z. It is reached only for a hit on the far half of a diagonal-split cell, and it
	/// shifts the reported point rather than whether there was one.</para>
	/// </summary>
	/// <returns>The world-space hit point, or null if neither triangle's plane meets the segment.</returns>
	private Vec3i? SurfaceIntersection(int cellX, int cellY, int fromX, int fromY, int fromZ,
			int toX, int toY, int toZ) {
		int cell = CornerIndex(cellX, cellY);
		if (cell < 0) {
			return null;
		}

		int cellSize = 1 << CellShift;
		int originX = cellX << CellShift;
		int originY = cellY << CellShift;

		// Everything below works in cell-local X/Y with world Z, which is the frame the face normals
		// were built in.
		int startX = fromX - originX;
		int startY = fromY - originY;
		int endX = toX - originX;
		int endY = toY - originY;

		int at = cell * 6;
		int normalX = _normals[at];
		int normalY = _normals[at + 1];
		int normalZ = _normals[at + 2];
		int planeD = -(_rawHeights[cell] * HeightScale + HeightBase) * normalZ;

		int selector = _diagonals[cell];
		if (PlanePoint(normalX, normalY, normalZ, planeD, startX, startY, fromZ, endX, endY, toZ,
				out int hitX, out int hitY, out int hitZ)) {
			bool inNearTriangle = selector switch {
				0 => hitX + hitY <= cellSize,
				1 => true,
				2 => hitX < hitY,
				_ => false,
			};

			if (inNearTriangle) {
				return new Vec3i(hitX + originX, hitY + originY, hitZ);
			}
		}

		// Selector 1 is a cell whose four corners are coplanar: there is no second triangle.
		if (selector == 1) {
			return null;
		}

		normalX = _normals[at + 3];
		normalY = _normals[at + 4];
		normalZ = _normals[at + 5];

		if (selector == 0) {
			int eastCell = CornerIndex(cellX + 1, cellY);
			if (eastCell < 0) {
				return null;
			}

			planeD = -(_rawHeights[eastCell] * HeightScale + HeightBase) * normalZ;
			startX -= cellSize;
			endX -= cellSize;
			originX += cellSize;
		}

		if (!PlanePoint(normalX, normalY, normalZ, planeD, startX, startY, fromZ, endX, endY, toZ,
				out hitX, out hitY, out hitZ)) {
			return null;
		}

		return new Vec3i(hitX + originX, hitY + originY, hitZ);
	}

	/// <summary>
	/// <c>Math_PlaneSegmentIntersect</c> (<c>0047e504</c>) — where a segment crosses a plane, or false if it does not reach it. The
	/// parameter along the segment is kept as an exact numerator/denominator pair and applied to
	/// each component through a 64-bit multiply and divide, so nothing is lost to an intermediate.
	/// </summary>
	internal static bool PlanePoint(int normalX, int normalY, int normalZ, int planeD,
			int fromX, int fromY, int fromZ, int toX, int toY, int toZ,
			out int hitX, out int hitY, out int hitZ) {
		hitX = 0;
		hitY = 0;
		hitZ = 0;

		int runX = toX - fromX;
		int runY = toY - fromY;
		int runZ = toZ - fromZ;

		int denominator = normalX * runX + normalY * runY + normalZ * runZ;
		if (denominator == 0) {
			return false;
		}

		int numerator = -(normalX * fromX + normalY * fromY + normalZ * fromZ + planeD);

		// The crossing has to land within the segment. The comparisons flip with the denominator's
		// sign, which is how the original decides that without dividing first.
		if (denominator < 0) {
			if (numerator > 0 || numerator < denominator) {
				return false;
			}
		} else if (numerator < 0 || denominator < numerator) {
			return false;
		}

		hitX = (int)((long)runX * numerator / denominator) + fromX;
		hitY = (int)((long)runY * numerator / denominator) + fromY;
		hitZ = (int)((long)runZ * numerator / denominator) + fromZ;
		return true;
	}

	/// <summary>
	/// The original's corner addressing and its bounds test: a flat index, valid only within
	/// <c>1 &lt;&lt; (WidthShift + HeightShift)</c>, and -1 where the original produces a null
	/// pointer. The two-argument form of <see cref="Corner"/>, carrying the same column wrap.
	/// </summary>
	private int CornerIndex(int cellX, int cellY) => Corner(cellX + (cellY << WidthShift));

	/// <summary>
	/// <c>Terrain_FaceBlocksMovement</c> (<c>0046fe40</c>) — whether one triangle blocks movement, on the <b>steepness of its
	/// normal alone</b>: an upward component under <see cref="FaceBlockingNormalZ"/> is a wall and
	/// stops the segment, anything shallower does not.
	///
	/// <para>The equality case is transcribed rather than tidied. The original compares twice against
	/// the same immediate — below it blocks, above it does not, and on it exactly it falls through to
	/// a dot product of the face normal against the segment's direction, blocking only when the
	/// segment is running into the face hard enough. That third arm can only be reached by a normal
	/// landing on the boundary value to the unit, so it decides almost nothing, but it is what the
	/// binary does.</para>
	/// </summary>
	private bool FaceBlocks(int normalAt, int dirX, int dirY, int dirZ) {
		int normalZ = _normals[normalAt + 2];

		if (normalZ < FaceBlockingNormalZ) {
			return true;
		}

		if (normalZ > FaceBlockingNormalZ) {
			return false;
		}

		return _normals[normalAt] * dirX + _normals[normalAt + 1] * dirY + normalZ * dirZ
			< FaceHeadOnDot;
	}

	/// <summary>
	/// <c>Terrain_FaceBlocksAt</c> (<c>0046fe84</c>) asked with no direction, which is how
	/// <c>Deployment_PickPointNearPlayer</c> (<c>0042354c</c>) asks it: a zero direction makes the
	/// head-on dot product zero, so the test reduces to the steepness of the face under the point,
	/// and a point off the grid blocks. This is "can something stand here".
	/// </summary>
	public bool BlocksMovementAt(int worldX, int worldY) =>
		FaceBlocksAt(worldX, worldY, 0, 0, 0);

	/// <summary>
	/// <c>Terrain_FaceBlocksAt</c> (<c>0046fe84</c>) — the same test applied at a world point: find its cell, pick which of the
	/// cell's two triangles the point falls in by the same diagonal split the height query uses, and
	/// ask <see cref="FaceBlocks"/>. A point off the grid blocks, as the original's null cell does —
	/// "off the grid" by the original's flat index, so a point just past the west or east edge reads
	/// the cell at the far end of the neighbouring row, the wrap <see cref="CornerIndex"/> carries.
	/// </summary>
	private bool FaceBlocksAt(int worldX, int worldY, int dirX, int dirY, int dirZ) {
		int cell = CornerIndex(worldX >> CellShift, worldY >> CellShift);
		if (cell < 0) {
			return true;
		}

		int fracX = worldX & ((1 << CellShift) - 1);
		int fracY = worldY & ((1 << CellShift) - 1);

		int split = _diagonals[cell] & 3;
		bool farTriangle = split switch {
			0 => fracX + fracY > 1 << CellShift,
			2 => fracY <= fracX,
			_ => false
		};

		return FaceBlocks(cell * 6 + (farTriangle ? 3 : 0), dirX, dirY, dirZ);
	}

	/// <summary>
	/// <c>Terrain_EdgeFaceBlocks</c> (<c>0046ff74</c>) — the test for a step that leaves one cell across a named edge. It asks
	/// the triangle on this side of the edge first and the neighbouring cell's triangle second, so a
	/// segment is stopped by whichever face it meets. Which of a cell's two triangles borders which
	/// edge depends on the diagonal, which is why the north and south arms consult it and the east
	/// and west arms do not. The neighbour is found by the original's flat index, so across the west
	/// or east edge of the grid it is the far end of the neighbouring row.
	/// </summary>
	/// <returns>
	/// <see cref="NotBlocked"/>, <see cref="BlockedThisCell"/> when this cell's own face stops it, or
	/// <see cref="BlockedNextCell"/> when the neighbour's does — which is what chooses between the
	/// step's two ends as the hit point.
	/// </returns>
	private int EdgeFaceBlocks(int cellX, int cellY, int edge, int dirX, int dirY, int dirZ) {
		// (this cell, its triangle) then (the neighbour, its triangle). A negative triangle index
		// means "the one the diagonal puts against this edge", resolved per cell below.
		var (nearOffset, farOffset, neighbourX, neighbourY, byDiagonal) = edge switch {
			EdgeWest => (0, 3, cellX - 1, cellY, false),
			EdgeEast => (3, 0, cellX + 1, cellY, false),
			EdgeNorth => (0, 0, cellX, cellY + 1, true),
			_ => (0, 0, cellX, cellY - 1, true)
		};

		int here = CornerIndex(cellX, cellY);
		if (here < 0) {
			return BlockedNextCell;
		}

		int nearAt = here * 6 + (byDiagonal
			? (_diagonals[here] & 3) == 0
				? edge == EdgeNorth ? 3 : 0
				: edge == EdgeNorth ? 0 : 3
			: nearOffset);

		if (FaceBlocks(nearAt, dirX, dirY, dirZ)) {
			return BlockedThisCell;
		}

		int there = CornerIndex(neighbourX, neighbourY);
		if (there < 0) {
			return BlockedNextCell;
		}

		int farAt = there * 6 + (byDiagonal
			? (_diagonals[there] & 3) == 0
				? edge == EdgeNorth ? 0 : 3
				: edge == EdgeNorth ? 3 : 0
			: farOffset);

		return FaceBlocks(farAt, dirX, dirY, dirZ) ? BlockedNextCell : NotBlocked;
	}

	/// <summary>
	/// <c>Terrain_FindDiagonalCrossing</c> (<c>0046fcac</c>) — where a step from
	/// (<paramref name="fromX"/>, <paramref name="fromY"/>) to (<paramref name="toX"/>,
	/// <paramref name="toY"/>) inside one cell enters the triangle its far end lies in: the step's own
	/// start when both ends are on the same side of the cell's diagonal, otherwise the point where it
	/// crosses the diagonal, found by at most eight bisections and reported with <c>z</c> 0.
	///
	/// <para>The test is done in cell-local coordinates with <c>x</c> mirrored across the cell for the
	/// anti-diagonal split (selector 0), so one comparison, <c>y &lt; x</c>, serves both
	/// diagonals.</para>
	/// </summary>
	private Vec3i DiagonalCrossing(int cellX, int cellY, int fromX, int fromY, int fromZ, int toX, int toY) {
		int cellSize = 1 << CellShift;
		int originX = cellX << CellShift;
		int originY = cellY << CellShift;
		bool antiDiagonal = (_diagonals[CornerIndex(cellX, cellY)] & 3) == 0;

		int nearX = fromX - originX, nearY = fromY - originY;
		int farX = toX - originX, farY = toY - originY;
		if (antiDiagonal) {
			nearX = cellSize - nearX;
			farX = cellSize - farX;
		}

		bool nearBelow = nearY < nearX;
		if (farY < farX == nearBelow) {
			return new Vec3i(fromX, fromY, fromZ);
		}

		int midX = 0, midY = 0;
		for (int i = 0; i < 8; i++) {
			midX = (farX + nearX) >> 1;
			midY = (farY + nearY) >> 1;
			if (midY == midX) {
				break;
			}

			if (midY < midX == nearBelow) {
				nearX = midX;
				nearY = midY;
			} else {
				farX = midX;
				farY = midY;
			}
		}

		if (antiDiagonal) {
			midX = cellSize - midX;
		}

		return new Vec3i(midX + originX, midY + originY, 0);
	}

	/// <summary>
	/// The upward component, at <see cref="NormalOne"/> scale, below which a face is a wall movement
	/// cannot cross — about 41° of slope.
	/// </summary>
	private const int FaceBlockingNormalZ = 0x60e;

	/// <summary>The dot product a segment must be under to count as running into a boundary face.</summary>
	private const int FaceHeadOnDot = -8000000;

	/// <summary>Neither face across the edge stops the segment.</summary>
	private const int NotBlocked = 0;

	/// <summary>The neighbouring cell's face stops it; the step's far end is the hit.</summary>
	private const int BlockedNextCell = 1;

	/// <summary>This cell's own face stops it; the step's near end is the hit.</summary>
	private const int BlockedThisCell = 2;
}

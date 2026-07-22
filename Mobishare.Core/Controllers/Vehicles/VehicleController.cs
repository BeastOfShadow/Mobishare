using System;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Mobishare.Core.Models.Vehicles;
using Mobishare.Core.Requests.Vehicles.VehicleRequests.Commands;
using Mobishare.Core.Requests.Vehicles.VehicleRequests.Queries;
using Swashbuckle.AspNetCore.Annotations;

namespace Mobishare.Core.Controllers.Vehicles;

[ApiController]
[Route("api/[controller]")]
public class VehicleController : ControllerBase
{
    private readonly IMediator _mediator;

    public VehicleController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpPost()]
    [SwaggerOperation(
        Summary = "Create a new vehicle",
        Description = "This endpoint allows you to create a new vehicle.",
        OperationId = "CreateVehicle")]
    [SwaggerResponse(201, "Vehicle created successfully", typeof(CreateVehicle))]
    [SwaggerResponse(400, "Invalid request payload")]
    public async Task<IActionResult> CreateVehicle(
        [FromBody] 
        [SwaggerParameter(Description = "The vehicle details to create.", Required = true)]
        CreateVehicle request
    )
    {
        if (request == null)
        {
            return BadRequest("Request payload cannot be null.");
        }

        var result = await _mediator.Send(request);
        if (result == null)
        {
            return BadRequest("Failed to create vehicle.");
        }

        return CreatedAtAction(nameof(GetVehicleById), new { id = result.Id }, result);
    }


    [HttpPut()]
    [SwaggerOperation(
        Summary = "Update an existing vehicle",
        Description = "This endpoint allows you to update an existing vehicle.",
        OperationId = "UpdateVehicle")]
    [SwaggerResponse(200, "Vehicle updated successfully", typeof(UpdateVehicle))]
    [SwaggerResponse(400, "Invalid request payload")]
    [SwaggerResponse(404, "Vehicle not found")]
    public async Task<IActionResult> UpdateVehicle(
        [FromBody]
        [SwaggerParameter(Description = "The vehicle details to update.", Required = true)]
        UpdateVehicle request)
    {
        if (request == null)
        {
            return BadRequest("Request payload cannot be null.");
        }

        var result = await _mediator.Send(request);
        
        return Ok(result);
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(
        Summary = "Delete a vehicle",
        Description = "This endpoint allows you to delete a vehicle by its ID."
    )]
    [SwaggerResponse(200, "Vehicle deleted successfully")]
    [SwaggerResponse(404, "Vehicle not found")]
    [SwaggerResponse(500, "Internal server error")]
    public async Task<IActionResult> DeleteVehicle(
        [FromRoute]
        [SwaggerParameter("The ID of the vehicle to delete", Required = true)]
        int id)
    {
        if (id <= 0)
        {
            return BadRequest("Invalid vehicle ID.");
        }

        var response = await _mediator.Send(new DeleteVehicle { Id = id });

        return NoContent();
    }

    [HttpGet("AllVehicles")]
    [SwaggerOperation(
        Summary = "Get all vehicles",
        Description = "This endpoint retrieves all vehicles.",
        OperationId = "GetAllVehicles")]
    [SwaggerResponse(200, "Vehicles retrieved successfully", typeof(IEnumerable<Vehicle>))]
    public async Task<IActionResult> GetAllVehicles()
    {
        var result = await _mediator.Send(new GetAllVehicles());

        return Ok(result);
    }

    [HttpGet("{id}")]
    [SwaggerOperation(
        Summary = "Get vehicle by ID",
        Description = "This endpoint retrieves a vehicle by its ID.",
        OperationId = "GetVehicleById")]
    [SwaggerResponse(200, "Vehicle retrieved successfully", typeof(Vehicle))]
    [SwaggerResponse(404, "Vehicle not found")]
    // Anonymous on purpose: called by the MQTT-ingestion background service
    // (Mobishare.Infrastructure/Services/Mqtt/MqttMessageHandler.cs), which has
    // no HttpContext/user to authenticate as. Read-only, low sensitivity.
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> GetVehicleById(
        [FromRoute]
        [SwaggerParameter(Description = "The ID of the vehicle to retrieve.", Required = true)]
        int id)
    {
        if (id <= 0)
        {
            return BadRequest("Invalid vehicle ID.");
        }

        var result = await _mediator.Send(new GetVehicleById(id));

        return Ok(result);
    }

    [HttpGet("GetAvailableVehicles")]
    [SwaggerOperation(
        Summary = "Get all available vehicles",
        Description = "This endpoint retrieves all available vehicles.",
        OperationId = "GetAllAvailableVehicles")]
    [SwaggerResponse(200, "Available vehicles retrieved successfully", typeof(IEnumerable<Vehicle>))]
    public async Task<IActionResult> GetAllAvailableVehicles()
    {
        var result = await _mediator.Send(new GetAllAvailableVehicles());

        return Ok(result);
    }
}
